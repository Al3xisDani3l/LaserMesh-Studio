// SPDX-License-Identifier: GPL-3.0-or-later
// Adapted from PyroGraph contributors' protocol.py (see NOTICE.md).
using System.Buffers.Binary;
using System.Text;

namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Interprets replies matched to a query by the caller.</summary>
public static class LaserPeckerReplies
{
    /// <summary>Decodes working status and optional firmware fields.</summary>
    public static WorkingStatus Status(LaserPeckerFrame frame)
    {
        var p = Require(frame, 0, 21);
        return new(p[0], p[1], p[2], p[3], p[4], p[5],
            BinaryPrimitives.ReadUInt32BigEndian(p[7..]), p[11], p[13], p[14], p[15], p[16],
            p[17], p[18], p[19], p[20], At(p, 21), At(p, 22), At(p, 25), At(p, 26));
    }

    /// <summary>Decodes software version and the raw and unpadded hexadecimal hardware identifiers.</summary>
    public static (ushort Software, uint Hardware, byte[] HardwareBytes) Version(LaserPeckerFrame frame)
    {
        var p = Require(frame, 0, 6);
        uint hardware = 0;
        foreach (var value in p.Slice(2, 4))
            hardware = (hardware << (value < 16 ? 4 : 8)) | value;
        return (BinaryPrimitives.ReadUInt16BigEndian(p), hardware, p.Slice(2, 4).ToArray());
    }

    /// <summary>Decodes the MAC as six uppercase hexadecimal octets.</summary>
    public static string Mac(LaserPeckerFrame frame)
        => string.Join(':', Require(frame, 0, 6)[..6].ToArray().Select(x => x.ToString("X2")));

    /// <summary>Decodes the UTF-8 device name, when the device provides it.</summary>
    public static string Name(LaserPeckerFrame frame)
    {
        var p = Require(frame, 0, 2);
        return p.Length == 2 ? "" : Encoding.UTF8.GetString(p[2..^1]).Trim();
    }

    /// <summary>Decodes file IDs and validates the returned count.</summary>
    public static uint[] FileIds(LaserPeckerFrame frame)
    {
        var p = Require(frame, 0, 2);
        // PyroGraph ignores the final byte and any incomplete ID in the trailer.
        var count = (p.Length - 2) / 4;
        if (count != p[0])
            throw new InvalidDataException("Cantidad de archivos inconsistente.");
        var ids = new uint[count];
        for (var i = 0; i < ids.Length; i++)
            ids[i] = BinaryPrimitives.ReadUInt32BigEndian(p[(1 + i * 4)..]);
        return ids;
    }

    /// <summary>Decodes the result byte of a file acknowledgement. One means success.</summary>
    public static byte FileAcknowledgement(LaserPeckerFrame frame) => Require(frame, 5, 2)[1];

    private static ReadOnlySpan<byte> Require(LaserPeckerFrame frame, byte function, int minimum)
        => frame.Function == function && frame.Payload.Length >= minimum
            ? frame.Payload.Span : throw new InvalidDataException("Respuesta LaserPecker incompatible.");

    private static byte? At(ReadOnlySpan<byte> payload, int index)
        => index < payload.Length ? payload[index] : null;
}

/// <summary>Working status reported by LaserPecker.</summary>
/// <param name="Mode">Working mode.</param>
/// <param name="WorkState">State within the working mode.</param>
/// <param name="Progress">Raw progress byte.</param>
/// <param name="Power">Raw laser power.</param>
/// <param name="Speed">Raw speed.</param>
/// <param name="Error">Error identifier.</param>
/// <param name="FileId">Current file identifier.</param>
/// <param name="Temperature">Raw temperature.</param>
/// <param name="StandConnection">Stand connection byte.</param>
/// <param name="Passes">Print passes.</param>
/// <param name="Angle">Raw angle.</param>
/// <param name="MotorState">Motor state.</param>
/// <param name="RotaryConnection">Rotary connection byte.</param>
/// <param name="SlideConnection">Slide connection byte.</param>
/// <param name="CarConnection">Car connection byte.</param>
/// <param name="ActiveLink">Active transport byte.</param>
/// <param name="Stop">Optional stop flag.</param>
/// <param name="SafeKey">Optional safety key flag.</param>
/// <param name="CoverConnection">Optional cover connection byte.</param>
/// <param name="Standard">Optional standard indicator.</param>
public sealed record WorkingStatus(byte Mode, byte WorkState, byte Progress, byte Power, byte Speed,
    byte Error, uint FileId, byte Temperature, byte StandConnection, byte Passes, byte Angle,
    byte MotorState, byte RotaryConnection, byte SlideConnection, byte CarConnection, byte ActiveLink,
    byte? Stop, byte? SafeKey, byte? CoverConnection, byte? Standard)
{
    /// <summary>Indicates an idle reply without the paused state.</summary>
    public bool Finished => Mode == 6 && WorkState != 4;
}
