using System.Numerics;

namespace LaserMesh.Geometry;

/// <summary>Immutable triangles with flat normals in object space, in millimetres.</summary>
public sealed class TriangleMesh
{
    /// <summary>Maximum triangles accepted for one mesh.</summary>
    public const int MaxTriangles = 1_000_000;

    private readonly Vector3[] positions;
    private readonly Vector3[] normals;

    internal TriangleMesh(Vector3[] positions, CancellationToken cancellationToken)
    {
        if (positions.Length < 3 || positions.Length % 3 != 0 || positions.Length / 3 > MaxTriangles)
            throw new InvalidDataException("Número de triángulos fuera de rango.");

        this.positions = positions;
        normals = new Vector3[positions.Length / 3];
        var unique = new HashSet<Vector3>();
        var min = positions[0];
        var max = min;
        double x = 0, y = 0, z = 0, area = 0;
        var highestFaceZ = double.NegativeInfinity;
        var highestFace = -1;

        for (var i = 0; i < positions.Length; i += 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var point in positions.AsSpan(i, 3))
            {
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)
                    || Math.Abs(point.X) > 1e9f || Math.Abs(point.Y) > 1e9f || Math.Abs(point.Z) > 1e9f)
                    throw new InvalidDataException("Coordenada fuera del rango admitido de ±10⁹ mm.");
                unique.Add(point);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            var a = positions[i];
            var b = positions[i + 1];
            var c = positions[i + 2];
            var u = b - a;
            var v = c - a;
            var nx = (double)u.Y * v.Z - (double)u.Z * v.Y;
            var ny = (double)u.Z * v.X - (double)u.X * v.Z;
            var nz = (double)u.X * v.Y - (double)u.Y * v.X;
            var weight = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (weight == 0)
                continue;
            normals[i / 3] = new((float)(nx / weight), (float)(ny / weight), (float)(nz / weight));
            var faceZ = ((double)a.Z + b.Z + c.Z) / 3;
            if (Math.Abs(nz / weight) >= 0.1 && faceZ > highestFaceZ)
            {
                highestFaceZ = faceZ;
                highestFace = i / 3;
            }
            x += ((double)a.X + b.X + c.X) * weight;
            y += ((double)a.Y + b.Y + c.Y) * weight;
            z += ((double)a.Z + b.Z + c.Z) * weight;
            area += weight;
        }

        if (area == 0)
            throw new InvalidDataException("La malla no tiene triángulos con área.");
        Minimum = min;
        Maximum = max;
        Centroid = new((float)(x / (3 * area)), (float)(y / (3 * area)), (float)(z / (3 * area)));
        VertexCount = unique.Count;
        HighestProjectableFace = highestFace;
    }

    /// <summary>Vertex positions in triangle order.</summary>
    public ReadOnlySpan<Vector3> Positions => positions;

    /// <summary>One normal per triangle. Degenerate triangles have zero normals.</summary>
    public ReadOnlySpan<Vector3> FaceNormals => normals;

    /// <summary>Number of triangle faces.</summary>
    public int TriangleCount => normals.Length;

    /// <summary>Number of distinct vertex positions.</summary>
    public int VertexCount { get; }

    /// <summary>Index of the highest face by centroid with an absolute unit normal Z of at least 0.1, or -1.</summary>
    public int HighestProjectableFace { get; }

    /// <summary>Minimum corner of the axis-aligned bounding box.</summary>
    public Vector3 Minimum { get; }

    /// <summary>Maximum corner of the axis-aligned bounding box.</summary>
    public Vector3 Maximum { get; }

    /// <summary>Object-space dimensions in millimetres.</summary>
    public Vector3 Dimensions => Maximum - Minimum;

    /// <summary>Area-weighted surface centroid in millimetres.</summary>
    public Vector3 Centroid { get; }
}
