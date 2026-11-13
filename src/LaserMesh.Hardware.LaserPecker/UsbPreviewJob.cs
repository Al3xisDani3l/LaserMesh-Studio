using System.Globalization;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Validated LP2 preview data in its fixed, 100 mm firmware 3.16 coordinate field.</summary>
public sealed record UsbPreviewJob(byte[] Start, byte[] Stop, byte[]? File)
{
    /// <summary>Builds a rectangle or low-level vector preview from closed scene XY loops in millimetres.</summary>
    public static UsbPreviewJob Create(IReadOnlyList<IReadOnlyList<Vector2>> loops, bool rectangle)
    {
        ArgumentNullException.ThrowIfNull(loops);
        if (loops.Count == 0 || loops.Any(loop => loop.Count < 3))
            throw new ArgumentException("La guía necesita perímetros cerrados.", nameof(loops));
        var points = loops.SelectMany(loop => loop).ToArray();
        if (points.Length > 100_000 || points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)))
            throw new ArgumentException("Guía inválida o demasiado compleja.", nameof(loops));
        var min = points.Aggregate(Vector2.Min);
        var max = points.Aggregate(Vector2.Max);
        var rect = RectanglePreviewProtocol.StartCentered(min.X, min.Y, max.X, max.Y);
        if (rectangle)
            return new(rect, RectanglePreviewProtocol.Stop(), null);

        var text = new StringBuilder("M05 S0\nG90\nG21\n");
        foreach (var loop in loops)
        {
            text.Append("G1 F3000\n");
            Move(loop[0]);
            text.Append("G4 P0\nM03 S1\nG4 P0\nG1 F1200\n");
            foreach (var point in loop.Skip(1))
                Move(point);
            Move(loop[0]);
            text.Append("G4 P0\nM05 S0\n");
        }
        // LP2 caches content by ID and may retain the old file when an ID is reused.
        // Derive identity from the complete encoded program, including pose and offsets.
        var program = Encoding.UTF8.GetBytes(text.ToString());
        var digest = SHA256.HashData(program);
        var fileId = BinaryPrimitives.ReadUInt32BigEndian(digest);
        if (fileId == 0)
            fileId = 1;
        return new(AndroidVectorPreviewProtocol.Start(fileId, 0, 0), AndroidVectorPreviewProtocol.Stop(),
            AndroidVectorPreviewProtocol.EncodeFile(fileId, "LaserMesh-placement", program));

        void Move(Vector2 p) => text.Append(CultureInfo.InvariantCulture, $"G1 X{50 + p.X:0.###} Y{50 - p.Y:0.###}\n");
    }
}
