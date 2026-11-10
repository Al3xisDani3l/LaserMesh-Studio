// SPDX-License-Identifier: GPL-3.0-or-later
// Adapted from PyroGraph contributors' MockTransport (see NOTICE.md).
namespace LaserMesh.Hardware.LaserPecker;

/// <summary>In-memory LP2 simulation for queries and rectangular preview frames.</summary>
public sealed class SimulatedTransport
{
    private static readonly byte[] PreviewStop = RectanglePreviewProtocol.Stop();
    private bool connected = true;
    private bool previewing;

    /// <summary>Simulates the loss of this transport. Create a new instance for a new session.</summary>
    public void Disconnect() => connected = false;

    /// <summary>Returns a simulated reply or null for unsupported requests.</summary>
    public byte[]? Exchange(ReadOnlySpan<byte> request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!connected)
            throw new IOException("Transporte simulado desconectado. Estado de previsualización desconocido.");
        if (request.SequenceEqual(PreviewStop))
        {
            previewing = false;
            return null;
        }
        if (!LaserPeckerProtocol.TryDecode(request, out var frame))
            return null;
        if (frame.Function == 2 && frame.Payload is { Length: 14 }
            && frame.Payload.Span[0] == 2 && frame.Payload.Span[11] == 1)
        {
            previewing = true;
            return null;
        }
        if (frame.Function != 0 || frame.Payload.Length != 5)
            return null;

        return frame.Payload.Span[0] switch
        {
            0 => LaserPeckerProtocol.Encode(0, [previewing ? (byte)2 : (byte)6, previewing ? (byte)2 : (byte)0, .. new byte[19]]),
            1 => LaserPeckerProtocol.Encode(0, [0, 0, 0]),
            3 => LaserPeckerProtocol.Encode(0, [1, 60, 1, 0x32, 0x1e, 0x86, 0, 3]),
            5 => LaserPeckerProtocol.Encode(0, [0xdc, 0x0d, 0x30, 0xaa, 0xbb, 0xcc, 0, 5]),
            _ => null
        };
    }
}
