using System.Numerics;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Channels;
using LaserMesh.Hardware.LaserPecker;
using Xunit;

namespace LaserMesh.Tests;

public sealed class UsbPreviewTests
{
    private static readonly IReadOnlyList<IReadOnlyList<Vector2>> Outline =
        [new Vector2[] { new(-4, -3), new(4, -3), new(4, 1), new(-1, 1), new(-1, 3), new(-4, 3) }];

    [Fact]
    public void VectorUsesCentralMillimetresAndDisablesLaserBetweenLoops()
    {
        var job = UsbPreviewJob.Create(Outline.Concat(Outline).ToArray(), false);
        var text = Encoding.UTF8.GetString(job.File![64..]);
        Assert.StartsWith("M05 S0\nG90\nG21\nG1 F3000\nG1 X46 Y53\n", text);
        Assert.Contains("G1 X54 Y49\nG1 X49 Y49\nG1 X49 Y47", text);
        Assert.Equal(2, text.Split("M03 S1").Length - 1);
        Assert.Contains("M05 S0\nG1 F3000", text);
        Assert.EndsWith("M05 S0\n", text);
        var rect = UsbPreviewJob.Create(Outline, true);
        Assert.Null(rect.File);
        Assert.Equal(RectanglePreviewProtocol.Start(46, 47, 8, 6), rect.Start);
        Assert.Throws<ArgumentOutOfRangeException>(() => UsbPreviewJob.Create(
            [new Vector2[] { new(49, 0), new(51, 0), new(49, 1) }], false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveUpdateStopsBeforeReplacementAndCancellationConfirmsIdle(bool rectangle)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var channel = Channel.CreateUnbounded<UsbPreviewJob>();
        var job = UsbPreviewJob.Create(Outline, rectangle);
        channel.Writer.TryWrite(job);
        var fake = new FakeConnection();
        fake.OnStart = count =>
        {
            if (count == 1) channel.Writer.TryWrite(job);
            else cancel.Cancel();
        };
        var session = new UsbPreviewSession();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync(() => fake, channel.Reader,
            new Progress<string>(), cancel.Token));
        Assert.Equal(new[] { "start", "stop", "start", "stop" }, fake.Events);
        Assert.False(session.UnknownState);
        Assert.True(fake.Disposed);
    }

    [Fact]
    public async Task ChangedOutlineStartsItsOwnContentEvenWhenDeviceKeepsOldFilesById()
    {
        var original = UsbPreviewJob.Create(Outline, false);
        IReadOnlyList<IReadOnlyList<Vector2>> changed =
            [new Vector2[] { new(-10, -10), new(10, -10), new(0, 10) }];
        var replacement = UsbPreviewJob.Create(changed, false);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var channel = Channel.CreateUnbounded<UsbPreviewJob>();
        channel.Writer.TryWrite(original);
        var fake = new FakeConnection();
        fake.OnStart = count =>
        {
            if (count == 1) channel.Writer.TryWrite(replacement);
            else cancel.Cancel();
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UsbPreviewSession().RunAsync(
            () => fake, channel.Reader, new Progress<string>(), cancel.Token));
        Assert.Equal(original.File![64..], fake.PreviewedFiles[0]);
        Assert.Equal(replacement.File![64..], fake.PreviewedFiles[1]);
    }

    [Fact]
    public void FileIdentityTracksEncodedGeometryAndMatchesStartFrame()
    {
        var original = UsbPreviewJob.Create(Outline, false);
        var identical = UsbPreviewJob.Create(Outline, false);
        IReadOnlyList<IReadOnlyList<Vector2>> moved = Outline.Select(loop =>
            (IReadOnlyList<Vector2>)loop.Select(p => p + new Vector2(1, 0)).ToArray()).ToArray();
        var translated = UsbPreviewJob.Create(moved, false);
        var id = BinaryPrimitives.ReadUInt32BigEndian(original.File!.AsSpan(5));
        Assert.NotEqual(0u, id);
        Assert.Equal(original.File, identical.File);
        Assert.NotEqual(id, BinaryPrimitives.ReadUInt32BigEndian(translated.File!.AsSpan(5)));
        foreach (var job in new[] { original, translated })
        {
            Assert.True(LaserPeckerProtocol.TryDecode(job.Start, out var frame));
            Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(job.File!.AsSpan(5)),
                BinaryPrimitives.ReadUInt32BigEndian(frame.Payload.Span[1..]));
        }
    }

    [Fact]
    public async Task LostConnectionBlocksAnotherStartWhenStopCannotBeConfirmed()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var channel = Channel.CreateUnbounded<UsbPreviewJob>();
        channel.Writer.TryWrite(UsbPreviewJob.Create(Outline, false));
        var fake = new FakeConnection { FailStop = true, OnStart = _ => cancel.Cancel() };
        var session = new UsbPreviewSession();
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => fake, channel.Reader, new Progress<string>(), cancel.Token));
        Assert.True(session.UnknownState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(
            () => throw new Exception("Must not reconnect"), channel.Reader, new Progress<string>(), CancellationToken.None));
    }

    [Fact]
    public async Task WrongFirmwareNeverStartsPreview()
    {
        var channel = Channel.CreateUnbounded<UsbPreviewJob>();
        channel.Writer.TryWrite(UsbPreviewJob.Create(Outline, true));
        var fake = new FakeConnection { Firmware = 315 };
        await Assert.ThrowsAsync<InvalidDataException>(() => new UsbPreviewSession().RunAsync(
            () => fake, channel.Reader, new Progress<string>(), CancellationToken.None));
        Assert.Empty(fake.Events);
    }

    private sealed class FakeConnection : IPreviewConnection
    {
        private readonly Queue<LaserPeckerFrame> replies = new();
        private bool active;
        private int starts;
        private uint pendingId;
        private readonly Dictionary<uint, byte[]> files = [];
        public List<byte[]> PreviewedFiles { get; } = [];
        public Action<int>? OnStart { get; set; }
        public bool FailStop { get; init; }
        public int Firmware { get; init; } = 316;
        public List<string> Events { get; } = [];
        public bool Disposed { get; private set; }

        public void Send(byte[] bytes)
        {
            if (!LaserPeckerProtocol.TryDecode(bytes, out var frame))
            {
                if (bytes[0] == 0x20)
                    pendingId = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5));
                else
                {
                    // Firmware retains its first stored content for a repeated ID.
                    files.TryAdd(pendingId, bytes.ToArray());
                    Reply(5, [2, 1, 0x25]);
                }
                return;
            }
            var p = frame.Payload.Span;
            if (frame.Function == 0)
            {
                if (p[0] == 3) Reply(0, [(byte)(Firmware >> 8), (byte)Firmware, 0, 0, 0, 0]);
                else
                {
                    var status = new byte[21];
                    status[0] = active ? (byte)2 : (byte)6;
                    status[1] = active ? (byte)1 : (byte)255;
                    status[20] = 2;
                    Reply(0, status);
                }
            }
            else if (frame.Function == 5) Reply(5, [1, 1, 0x25]);
            else if (frame.Function == 2 && p[0] is 1 or 2)
            {
                active = true;
                if (p[0] == 1)
                    PreviewedFiles.Add(files[BinaryPrimitives.ReadUInt32BigEndian(p[1..])]);
                Events.Add("start");
                OnStart?.Invoke(++starts);
            }
            else if (frame.Function == 0xFF || frame.Function == 2 && p[0] == 3)
            {
                Events.Add("stop");
                if (FailStop) throw new IOException("Disconnected");
                active = false;
            }
        }

        public LaserPeckerFrame Receive(Func<LaserPeckerFrame, bool> matches, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (replies.TryDequeue(out var reply))
                if (matches(reply)) return reply;
            throw new TimeoutException("No simulated reply");
        }
        private void Reply(byte function, byte[] payload)
        {
            Assert.True(LaserPeckerProtocol.TryDecode(LaserPeckerProtocol.Encode(function, payload), out var frame));
            replies.Enqueue(frame);
        }
        public void Dispose() => Disposed = true;
    }
}
