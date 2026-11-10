// SPDX-License-Identifier: GPL-3.0-or-later
// Adapted from PyroGraph contributors' MockTransport (see NOTICE.md).
namespace LaserMesh.Hardware.LaserPecker;

/// <summary>In-memory LP2 simulation for read-only queries.</summary>
public sealed class SimulatedTransport
{
    /// <summary>Returns a simulated reply or null for unsupported requests.</summary>
    public byte[]? Exchange(ReadOnlySpan<byte> request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!LaserPeckerProtocol.TryDecode(request, out var frame) || frame.Function != 0 || frame.Payload.Length != 5)
            return null;

        return frame.Payload.Span[0] switch
        {
            0 => LaserPeckerProtocol.Encode(0, [6, .. new byte[20]]),
            1 => LaserPeckerProtocol.Encode(0, [0, 0, 0]),
            3 => LaserPeckerProtocol.Encode(0, [1, 60, 1, 0x32, 0x1e, 0x86, 0, 3]),
            5 => LaserPeckerProtocol.Encode(0, [0xdc, 0x0d, 0x30, 0xaa, 0xbb, 0xcc, 0, 5]),
            _ => null
        };
    }
}
