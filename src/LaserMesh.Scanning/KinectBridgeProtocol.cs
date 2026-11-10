using System.Buffers.Binary;

namespace LaserMesh.Scanning;

/// <summary>Version-one envelope for local Kinect bridge requests and responses.</summary>
public static class KinectBridgeProtocol
{
    /// <summary>Depth frame from the Kinect V2 has 512 × 424 unsigned 16-bit millimetre values.</summary>
    public const int DepthFrameBytes = 512 * 424 * 2;

    /// <summary>Upper bound for one IPC payload, including uncompressed BGRA from Kinect V2.</summary>
    public const int MaxPayloadBytes = 8 * 1024 * 1024;

    private const int HeaderSize = 16;
    private const ushort Version = 1;

    /// <summary>Writes one complete envelope to a local stream. Serialize concurrent writes externally.</summary>
    public static async Task WriteAsync(Stream stream, BridgeMessage message, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(message.Payload.Length, MaxPayloadBytes);
        var header = new byte[HeaderSize];
        "LMKB"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)message.Kind);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), message.RequestId);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), message.Payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(message.Payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one envelope, validating the header before allocating the payload.</summary>
    public static async Task<BridgeMessage> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
        var kind = (BridgeMessageKind)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
        var id = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
        if (!header.AsSpan(0, 4).SequenceEqual("LMKB"u8) || version != Version
            || !Enum.IsDefined(kind) || length is < 0 or > MaxPayloadBytes)
            throw new InvalidDataException("Cabecera Kinect Bridge inválida.");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return new(kind, id, payload);
    }
}

/// <summary>Operation identifier within the Kinect IPC envelope.</summary>
public enum BridgeMessageKind : ushort
{
    /// <summary>Requests the sensor availability.</summary>
    GetStatus = 1,

    /// <summary>Returns the sensor availability.</summary>
    Status = 2,

    /// <summary>Requests a single depth capture.</summary>
    CaptureDepth = 3,

    /// <summary>Returns a depth capture.</summary>
    DepthFrame = 4,

    /// <summary>Returns a bridge failure.</summary>
    Error = 255
}

/// <summary>Message sent between the studio and the Kinect bridge.</summary>
/// <param name="Kind">Operation identifier.</param>
/// <param name="RequestId">Correlation identifier assigned by the caller.</param>
/// <param name="Payload">Binary payload.</param>
public readonly record struct BridgeMessage(BridgeMessageKind Kind, uint RequestId, ReadOnlyMemory<byte> Payload);
