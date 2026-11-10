// SPDX-License-Identifier: GPL-3.0-or-later
// Adapted from PyroGraph contributors' protocol.py (see NOTICE.md).
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Binary framing for LaserPecker.</summary>
public static class LaserPeckerProtocol
{
    /// <summary>Maximum command payload used by PyroGraph.</summary>
    public const int MaxCommandPayload = 249;

    /// <summary>Safety limit for a long reply body, including checksum.</summary>
    public const int MaxLongBody = 1024 * 1024;

    /// <summary>Encodes a short frame.</summary>
    public static byte[] Encode(byte function, ReadOnlySpan<byte> payload)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payload.Length, MaxCommandPayload);
        var frame = new byte[payload.Length + 6];
        frame[0] = 0xAA;
        frame[1] = 0xBB;
        frame[2] = (byte)(payload.Length + 3);
        frame[3] = function;
        payload.CopyTo(frame.AsSpan(4));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(frame.Length - 2), Checksum(frame.AsSpan(3, payload.Length + 1)));
        return frame;
    }

    /// <summary>Encodes 1-, 2- or 4-byte fields, retaining the low bits in big-endian order.</summary>
    public static byte[] EncodeFields(byte function, params (long Value, int Width)[] fields)
    {
        Span<byte> payload = stackalloc byte[MaxCommandPayload];
        var length = 0;
        foreach (var (value, width) in fields)
        {
            if (width is not (1 or 2 or 4))
                throw new ArgumentException("Ancho de campo no admitido.", nameof(fields));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length + width, payload.Length);
            for (var i = 0; i < width; i++)
                payload[length + width - i - 1] = (byte)(value >> (i * 8));
            length += width;
        }
        return Encode(function, payload[..length]);
    }

    /// <summary>Builds a read-only query.</summary>
    public static byte[] Query(byte state) => Encode(0, [state, 0, 0, 0, 0]);

    /// <summary>Builds a query for the named file list.</summary>
    public static byte[] QueryFileList(byte mount = 0) => Encode(0, [7, 0, mount, 0, 0]);

    /// <summary>Builds a query for one cached file ID.</summary>
    public static byte[] QueryCachedFile(uint fileId, byte mount = 1)
        => EncodeFields(0, (10, 1), (0, 1), (mount, 1), (fileId, 4));

    internal static int FrameSize(ReadOnlySpan<byte> prefix)
    {
        if (prefix.IsEmpty)
            return 0;
        if (prefix[0] != 0xAA)
            return -1;
        if (prefix.Length == 1)
            return 0;

        switch (prefix[1])
        {
            case 0xBB:
                if (prefix.Length < 3)
                    return 0;
                return prefix[2] >= 3 ? prefix[2] + 3 : -1;
            case 0xCC:
                if (prefix.Length < 6)
                    return 0;
                var body = BinaryPrimitives.ReadUInt32BigEndian(prefix[2..]);
                return body is >= 2 and <= MaxLongBody ? (int)body + 6 : -1;
            default:
                return -1;
        }
    }

    /// <summary>Decodes exactly one checksum-valid short or long frame.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, [NotNullWhen(true)] out LaserPeckerFrame? frame)
    {
        frame = null;
        if (FrameSize(data) != data.Length || data.Length < 6)
            return false;

        var isShort = data[1] == 0xBB;
        var body = data[(isShort ? 3 : 6)..^2];
        if (Checksum(body) != BinaryPrimitives.ReadUInt16BigEndian(data[^2..]))
            return false;

        frame = new(isShort ? body[0] : null, (isShort ? body[1..] : body).ToArray());
        return true;
    }

    private static ushort Checksum(ReadOnlySpan<byte> data)
    {
        var sum = 0;
        foreach (var value in data)
            sum += value;
        return (ushort)sum;
    }
}

/// <summary>A checksum-validated LP frame.</summary>
public sealed class LaserPeckerFrame
{
    internal LaserPeckerFrame(byte? function, byte[] payload)
    {
        Function = function;
        Payload = payload;
    }

    /// <summary>Function code, or null for the AA CC long format.</summary>
    public byte? Function { get; }

    /// <summary>Payload without header, function or checksum.</summary>
    public ReadOnlyMemory<byte> Payload { get; }
}
