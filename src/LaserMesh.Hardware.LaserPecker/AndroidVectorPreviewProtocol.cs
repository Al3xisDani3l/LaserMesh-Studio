using System.Buffers.Binary;
using System.Text;

namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Wire encoder for the G-code preview observed in LaserPecker Community 4.36.</summary>
/// <remarks>A centered stepped test was observed on LP2 firmware 3.16 over USB. Offsets are raw device fields.</remarks>
public static class AndroidVectorPreviewProtocol
{
    private const int FileHeaderSize = 64;
    private const int MaxDiagnosticGCodeBytes = 64 * 1024;

    /// <summary>Builds the unframed G-code file for offline comparison.</summary>
    public static byte[] EncodeFile(uint fileId, string name, ReadOnlySpan<byte> gcodeUtf8)
    {
        ArgumentOutOfRangeException.ThrowIfZero(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var nameLength = Encoding.UTF8.GetByteCount(name);
        if (nameLength > 28 || name.Contains('\0'))
            throw new ArgumentException("El nombre admite hasta 28 bytes UTF-8 y debe ir sin NUL.", nameof(name));
        if (gcodeUtf8.Length is 0 or > MaxDiagnosticGCodeBytes)
            throw new ArgumentOutOfRangeException(nameof(gcodeUtf8));

        var file = new byte[FileHeaderSize + gcodeUtf8.Length];
        file[0] = 0x20;
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(5), fileId);
        Encoding.UTF8.GetBytes(name, file.AsSpan(34, nameLength));
        gcodeUtf8.CopyTo(file.AsSpan(FileHeaderSize));
        return file;
    }

    /// <summary>Announces a file length including its 64-byte header.</summary>
    public static byte[] AnnounceFile(int totalBytes)
    {
        if (totalBytes <= FileHeaderSize || totalBytes > FileHeaderSize + MaxDiagnosticGCodeBytes)
            throw new ArgumentOutOfRangeException(nameof(totalBytes));
        return LaserPeckerProtocol.EncodeFields(0x05, (1, 1), (totalBytes, 4), (0x25, 1));
    }

    /// <summary>Builds the exit frame used to close a successful Android preview upload.</summary>
    public static byte[] FinishFile() => Exit(0x19);

    /// <summary>Builds the vector-preview frame for a cached file. The preview level is the app's default value.</summary>
    public static byte[] Start(uint fileId, short x, short y)
    {
        ArgumentOutOfRangeException.ThrowIfZero(fileId);
        return LaserPeckerProtocol.EncodeFields(0x02, (1, 1), (fileId, 4),
            (x, 2), (y, 2), (0x60, 1), (4, 1), (1, 1), (0, 2));
    }

    /// <summary>Builds the exit frame for closing Android G-code preview.</summary>
    public static byte[] Stop() => Exit(0x40);

    private static byte[] Exit(byte tag) => LaserPeckerProtocol.Encode(0xFF, [tag, 0, 0, 0, 0]);
}
