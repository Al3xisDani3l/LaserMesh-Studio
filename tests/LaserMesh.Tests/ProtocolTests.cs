using System.Text.Json;
using LaserMesh.Hardware.LaserPecker;
using Xunit;

namespace LaserMesh.Tests;

public sealed class ProtocolTests
{
    private static readonly JsonElement Vectors = JsonSerializer.Deserialize<JsonElement>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "pyrograph-vectors.json")));

    [Theory]
    [InlineData("status")]
    [InlineData("version")]
    [InlineData("mac")]
    [InlineData("list")]
    [InlineData("cached")]
    [InlineData("fields")]
    [InlineData("max")]
    public void CommandsMatchPyroGraphByteForByte(string name)
    {
        var actual = name switch
        {
            "status" => LaserPeckerProtocol.Query(0),
            "version" => LaserPeckerProtocol.Query(3),
            "mac" => LaserPeckerProtocol.Query(5),
            "list" => LaserPeckerProtocol.QueryFileList(2),
            "cached" => LaserPeckerProtocol.QueryCachedFile(0xDEADBEEF, 2),
            "fields" => LaserPeckerProtocol.EncodeFields(6, (0xFF01, 1), (-2, 2), (0x01234567, 4)),
            "max" => LaserPeckerProtocol.Encode(1, Enumerable.Repeat((byte)255, 249).ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        Assert.Equal(Vector("commands", name), actual);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("version")]
    [InlineData("mac")]
    [InlineData("settings")]
    [InlineData("file_ids")]
    [InlineData("long_synthetic")]
    public void ValidRepliesDecodeAtEveryFragmentBoundary(string name)
    {
        var bytes = Vector("replies", name);
        Assert.True(LaserPeckerProtocol.TryDecode(bytes, out var whole));
        for (var split = 1; split < bytes.Length; split++)
        {
            var assembler = new FrameAssembler();
            Assert.Empty(assembler.Feed(bytes.AsSpan(0, split)));
            var frame = Assert.Single(assembler.Feed(bytes.AsSpan(split)));
            Assert.Equal(whole.Function, frame.Function);
            Assert.Equal(whole.Payload.ToArray(), frame.Payload.ToArray());
        }
    }

    [Fact]
    public void ParsesCapturedStatusVersionMacAndFileIds()
    {
        var status = LaserPeckerReplies.Status(Decode("status"));
        Assert.True(status.Finished);
        Assert.Equal(0, status.Error);
        Assert.Equal(1, status.ActiveLink);
        Assert.Null(status.CoverConnection);

        var version = LaserPeckerReplies.Version(Decode("version"));
        Assert.Equal(316, version.Software);
        Assert.Equal(0x1321E86u, version.Hardware);
        Assert.Equal(new byte[] { 1, 0x32, 0x1e, 0x86 }, version.HardwareBytes);
        Assert.Equal("DC:0D:30:AA:BB:CC", LaserPeckerReplies.Mac(Decode("mac")));

        var files = LaserPeckerReplies.FileIds(Decode("file_ids"));
        Assert.Equal(31, files.Length);
        Assert.Equal(0x000607FBu, files[0]);
        Assert.Equal(0x00060574u, files[^1]);
    }

    [Fact]
    public void SeparatesAdjacentFramesAndSkipsInvalidCandidates()
    {
        var status = Vector("replies", "status");
        var corrupt = (byte[])status.Clone();
        corrupt[^1]++;
        var longReply = Vector("replies", "long_synthetic");
        var assembler = new FrameAssembler();

        Assert.False(LaserPeckerProtocol.TryDecode(corrupt, out _));
        Assert.False(LaserPeckerProtocol.TryDecode([.. status, .. longReply], out _));
        var frames = assembler.Feed([0xFF, 0xAA, 0x11, 0xAA, 0xCC, 0xFF, 0xFF, 0xFF, 0xFF,
            .. corrupt, .. status, .. longReply]);
        Assert.Equal(2, frames.Count);
        Assert.Equal<byte?>(0, frames[0].Function);
        Assert.Null(frames[1].Function);
        Assert.Equal(new byte[] { 0x33, 0xAA, 0xBB, 0x08, 0xCC }, frames[1].Payload.ToArray());
    }

    [Fact]
    public void RejectsInvalidWidthsLengthsAndTruncatedReplies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LaserPeckerProtocol.Encode(0, new byte[250]));
        Assert.Throws<ArgumentException>(() => LaserPeckerProtocol.EncodeFields(0, (42, 3)));
        Assert.True(LaserPeckerProtocol.TryDecode(LaserPeckerProtocol.Query(0), out var query));
        Assert.Throws<InvalidDataException>(() => LaserPeckerReplies.Status(query));
    }

    [Fact]
    public void ResetRecoversFromTruncatedLongFrame()
    {
        var assembler = new FrameAssembler();
        Assert.Empty(assembler.Feed([0xaa, 0xcc, 0, 0, 1, 0, 7]));
        assembler.Reset();
        Assert.Single(assembler.Feed(Vector("replies", "version")));
    }

    [Fact]
    public void SimulationSupportsReadOnlyQueriesAndCancellation()
    {
        var simulated = new SimulatedTransport();
        var reply = simulated.Exchange(LaserPeckerProtocol.Query(0), default);
        Assert.True(LaserPeckerProtocol.TryDecode(reply, out var frame));
        Assert.True(LaserPeckerReplies.Status(frame).Finished);
        Assert.Null(simulated.Exchange(LaserPeckerProtocol.Encode(1, [1]), default));
        Assert.Throws<OperationCanceledException>(() => simulated.Exchange(LaserPeckerProtocol.Query(0), new(true)));
    }

    private static byte[] Vector(string group, string name)
        => Convert.FromHexString(Vectors.GetProperty(group).GetProperty(name).GetString()!);

    private static LaserPeckerFrame Decode(string name)
    {
        Assert.True(LaserPeckerProtocol.TryDecode(Vector("replies", name), out var frame));
        return frame;
    }
}
