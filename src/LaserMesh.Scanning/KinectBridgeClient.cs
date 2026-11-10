using System.IO.Pipes;
using System.Text;

namespace LaserMesh.Scanning;

/// <summary>Issues versioned requests to an explicitly started local Kinect bridge.</summary>
public sealed class KinectBridgeClient(string pipeName)
{
    /// <summary>Queries availability within the supplied deadline.</summary>
    public async Task<bool> IsAvailableAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var reply = await ExchangeAsync(BridgeMessageKind.GetStatus, BridgeMessageKind.Status, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (reply.Payload.Length != 1 || reply.Payload.Span[0] > 1)
            throw new InvalidDataException("Estado Kinect inválido.");
        return reply.Payload.Span[0] == 1;
    }

    /// <summary>Requests one depth image as row-major, little-endian millimetre values.</summary>
    public async Task<ReadOnlyMemory<byte>> CaptureDepthAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var reply = await ExchangeAsync(BridgeMessageKind.CaptureDepth, BridgeMessageKind.DepthFrame, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (reply.Payload.Length != KinectBridgeProtocol.DepthFrameBytes)
            throw new InvalidDataException("Tamaño de captura Kinect inválido.");
        return reply.Payload;
    }

    private async Task<BridgeMessage> ExchangeAsync(BridgeMessageKind request, BridgeMessageKind expected, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            // Each connection owns exactly one request.
            const uint id = 1;
            await pipe.ConnectAsync(token).ConfigureAwait(false);
            await KinectBridgeProtocol.WriteAsync(pipe, new(request, id, ReadOnlyMemory<byte>.Empty), token)
                .ConfigureAwait(false);
            var reply = await KinectBridgeProtocol.ReadAsync(pipe, token).ConfigureAwait(false);
            if (reply.RequestId != id)
                throw new InvalidDataException("La respuesta Kinect no corresponde a la solicitud.");
            if (reply.Kind == BridgeMessageKind.Error && reply.Payload.Length is > 0 and <= 1024)
                throw new IOException(Encoding.UTF8.GetString(reply.Payload.Span));
            if (reply.Kind != expected)
                throw new InvalidDataException("Tipo de respuesta Kinect inesperado.");
            return reply;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && token.IsCancellationRequested)
        {
            throw new TimeoutException("El Kinect Bridge no respondió a tiempo.", ex);
        }
    }
}
