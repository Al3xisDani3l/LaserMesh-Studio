using System.IO.Pipes;
using LaserMesh.Scanning;
using Xunit;

namespace LaserMesh.Tests;

public sealed class KinectBridgeTests
{
    [Fact]
    public async Task ClientQueriesAnInProcessNamedPipePeer()
    {
        var name = $"lasermesh-test-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var status = new KinectBridgeClient(name).IsAvailableAsync(TimeSpan.FromSeconds(10), timeout.Token);

        await server.WaitForConnectionAsync(timeout.Token);
        var request = await KinectBridgeProtocol.ReadAsync(server, timeout.Token);
        Assert.Equal(BridgeMessageKind.GetStatus, request.Kind);
        await KinectBridgeProtocol.WriteAsync(server, new(BridgeMessageKind.Status, request.RequestId, new byte[] { 1 }),
            timeout.Token);
        Assert.True(await status);
    }

    [Fact]
    public async Task ClientReportsMissingPeerWithinDeadline()
    {
        var client = new KinectBridgeClient($"lasermesh-missing-{Guid.NewGuid():N}");
        await Assert.ThrowsAsync<TimeoutException>(() => client.IsAvailableAsync(TimeSpan.FromMilliseconds(150), default));
    }

    [Fact]
    public async Task MessagesAreLengthDelimitedAndCorrelated()
    {
        using var stream = new MemoryStream();
        await KinectBridgeProtocol.WriteAsync(stream, new(BridgeMessageKind.GetStatus, 42, ReadOnlyMemory<byte>.Empty), default);
        await KinectBridgeProtocol.WriteAsync(stream, new(BridgeMessageKind.Status, 42, new byte[] { 1 }), default);
        Assert.Equal("4C4D4B42010001002A00000000000000", Convert.ToHexString(stream.ToArray()[..16]));
        stream.Position = 0;
        var request = await KinectBridgeProtocol.ReadAsync(stream, default);
        Assert.Equal(BridgeMessageKind.GetStatus, request.Kind);
        Assert.Equal(42u, request.RequestId);
        Assert.True(request.Payload.IsEmpty);
        var reply = await KinectBridgeProtocol.ReadAsync(stream, default);
        Assert.Equal(BridgeMessageKind.Status, reply.Kind);
        Assert.Equal(42u, reply.RequestId);
        Assert.Equal(new byte[] { 1 }, reply.Payload.ToArray());
    }

    [Theory]
    [InlineData("4C4D4B42020001000000000000000000")]
    [InlineData("4C4D4B420100010000000000FFFFFF7F")]
    [InlineData("4C4D4B420100FF7F0000000000000000")]
    public async Task InvalidHeaderIsRejected(string hex)
    {
        using var stream = new MemoryStream(Convert.FromHexString(hex));
        await Assert.ThrowsAsync<InvalidDataException>(() => KinectBridgeProtocol.ReadAsync(stream, default));
    }

    [Fact]
    public async Task IncompletePayloadIsRejected()
    {
        using var stream = new MemoryStream(Convert.FromHexString("4C4D4B42010002002A00000001000000"));
        await Assert.ThrowsAsync<EndOfStreamException>(() => KinectBridgeProtocol.ReadAsync(stream, default));
    }
}
