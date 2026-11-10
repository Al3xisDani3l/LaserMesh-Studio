// SPDX-License-Identifier: GPL-3.0-or-later
// Adapted from PyroGraph contributors' transport.py (see NOTICE.md).
namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Reassembles split or adjacent LP frames for a single reader.</summary>
public sealed class FrameAssembler
{
    private byte[] buffer = new byte[6];
    private int length;
    private int expected;

    /// <summary>Returns valid frames and retains any incomplete trailing frame.</summary>
    public IReadOnlyList<LaserPeckerFrame> Feed(ReadOnlySpan<byte> chunk)
    {
        var frames = new List<LaserPeckerFrame>();
        foreach (var value in chunk)
        {
            buffer[length++] = value;
            if (expected == 0)
            {
                var size = LaserPeckerProtocol.FrameSize(buffer.AsSpan(0, length));
                if (size < 0)
                {
                    // Keep a repeated prefix byte as the start of the next candidate.
                    length = value == 0xAA ? 1 : 0;
                    if (length == 1)
                        buffer[0] = value;
                    continue;
                }

                expected = size;
                if (expected > buffer.Length)
                    Array.Resize(ref buffer, expected);
            }

            if (expected == 0 || length != expected)
                continue;

            if (LaserPeckerProtocol.TryDecode(buffer.AsSpan(0, length), out var frame))
                frames.Add(frame);
            Reset();
        }
        return frames;
    }

    /// <summary>Discards an incomplete frame on timeout or disconnect.</summary>
    public void Reset()
    {
        length = 0;
        expected = 0;
    }
}
