using System.Globalization;
using System.Numerics;
using System.Text;

namespace LaserMesh.Geometry;

/// <summary>Reads ASCII and binary STL and writes binary STL.</summary>
public sealed class StlCodec
{
    private const int HeaderSize = 84;
    private const int RecordSize = 50;
    private const long MaxFileBytes = 256L * 1024 * 1024;

    /// <summary>Loads an STL on a background thread using the specified input-unit scale.</summary>
    public Task<TriangleMesh> ImportAsync(string path, float millimetresPerUnit, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            using var stream = File.OpenRead(path);
            return Read(stream, stream.Length, millimetresPerUnit, cancellationToken);
        }, cancellationToken);

    /// <summary>Reads a complete STL from the current stream position.</summary>
    public TriangleMesh Read(Stream stream, long length, float millimetresPerUnit, CancellationToken cancellationToken)
    {
        if (!float.IsFinite(millimetresPerUnit) || millimetresPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(millimetresPerUnit));
        if (length <= 0 || length > MaxFileBytes)
            throw new InvalidDataException("Tamaño de STL fuera de rango.");

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var prefix = reader.ReadBytes((int)Math.Min(length, HeaderSize));
        if (prefix.Length >= HeaderSize)
        {
            var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(80));
            if (length == HeaderSize + RecordSize * (long)count)
                return ReadBinary(reader, count, millimetresPerUnit, cancellationToken);
        }

        return ReadAscii(prefix, stream, millimetresPerUnit, cancellationToken);
    }

    /// <summary>Writes coordinates and face normals in binary STL format.</summary>
    public void WriteBinary(Stream stream, TriangleMesh mesh, CancellationToken cancellationToken)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(new byte[80]);
        writer.Write((uint)mesh.TriangleCount);
        for (var i = 0; i < mesh.TriangleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteVector(writer, mesh.FaceNormals[i]);
            foreach (var point in mesh.Positions.Slice(i * 3, 3))
                WriteVector(writer, point);
            writer.Write((ushort)0);
        }
    }

    private static TriangleMesh ReadBinary(BinaryReader reader, uint count, float scale, CancellationToken token)
    {
        if (count is 0 or > TriangleMesh.MaxTriangles)
            throw new InvalidDataException("Número de triángulos STL fuera de rango.");

        var positions = new Vector3[checked((int)count * 3)];
        for (var i = 0; i < positions.Length; i += 3)
        {
            token.ThrowIfCancellationRequested();
            ReadVector(reader);
            for (var j = 0; j < 3; j++)
                positions[i + j] = ReadVector(reader) * scale;
            reader.ReadUInt16();
        }
        return new(positions, token);
    }

    private static TriangleMesh ReadAscii(byte[] prefix, Stream stream, float scale, CancellationToken token)
    {
        // Replay the probe bytes without copying the rest of the input.
        // The prefix can split a UTF-8 name. Decode through one shared decoder.
        using var combined = new PrefixStream(prefix, stream);
        using var text = new StreamReader(combined);
        var vertices = new List<Vector3>();
        var line = NextLine(text);
        while (StartsWithWord(line, "solid"))
        {
            line = NextLine(text);
            while (StartsWithWord(line, "facet"))
            {
                token.ThrowIfCancellationRequested();
                ParseVector(line, "facet", "normal");
                RequireLine(text, "outer loop");
                if (vertices.Count >= TriangleMesh.MaxTriangles * 3)
                    throw new InvalidDataException("Número de triángulos STL fuera de rango.");
                for (var i = 0; i < 3; i++)
                    vertices.Add(ParseVector(NextLine(text), "vertex") * scale);
                RequireLine(text, "endloop");
                RequireLine(text, "endfacet");
                line = NextLine(text);
            }
            if (!StartsWithWord(line, "endsolid"))
                throw InvalidAscii();
            line = NextLine(text);
        }
        if (line is not null || vertices.Count == 0)
            throw InvalidAscii();
        return new(vertices.ToArray(), token);
    }

    private static string? NextLine(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                return line.Trim();
        }
        return null;
    }

    private static bool StartsWithWord(string? line, string word)
        => line is not null && line.StartsWith(word, StringComparison.OrdinalIgnoreCase)
            && (line.Length == word.Length || char.IsWhiteSpace(line[word.Length]));

    private static void RequireLine(StreamReader reader, string expected)
    {
        var parts = NextLine(reader)?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts is null || !string.Equals(string.Join(' ', parts), expected, StringComparison.OrdinalIgnoreCase))
            throw InvalidAscii();
    }

    private static Vector3 ParseVector(string? line, string first, string? second = null)
    {
        var parts = line?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var start = second is null ? 1 : 2;
        if (parts?.Length != start + 3 || !parts[0].Equals(first, StringComparison.OrdinalIgnoreCase)
            || (second is not null && !parts[1].Equals(second, StringComparison.OrdinalIgnoreCase))
            || !float.TryParse(parts[start], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[start + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !float.TryParse(parts[start + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            throw InvalidAscii();
        return new(x, y, z);
    }

    private static InvalidDataException InvalidAscii() => new("STL inválido o incompleto.");

    private static Vector3 ReadVector(BinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private sealed class PrefixStream(byte[] prefix, Stream rest) : Stream
    {
        private int offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int index, int count) => Read(buffer.AsSpan(index, count));
        public override int Read(Span<byte> buffer)
        {
            if (offset == prefix.Length)
                return rest.Read(buffer);
            var count = Math.Min(buffer.Length, prefix.Length - offset);
            prefix.AsSpan(offset, count).CopyTo(buffer);
            offset += count;
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
