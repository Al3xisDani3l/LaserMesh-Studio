using System.Numerics;
using HelixToolkit;
using HelixToolkit.SharpDX;
using LaserMesh.Engraving;

namespace LaserMesh.Rendering;

/// <summary>Converts engraving overlays to renderer geometry.</summary>
public static class EngravingGeometry
{
    /// <summary>Builds open bands above and below the reference height, plus their section outlines.</summary>
    public static (MeshGeometry3D? Above, MeshGeometry3D? Below, LineGeometry3D Base, LineGeometry3D? Section)
        CreateField(EngravingField field, double referenceZ)
    {
        var cut = Math.Clamp(referenceZ, 0, field.Height);
        return (CreateBand(field, cut, field.Height), CreateBand(field, 0, cut),
            CreateOutline(field, 0), cut > 0 && cut < field.Height ? CreateOutline(field, cut) : null);
    }

    private static Vector3[] Corners(EngravingField field, double z)
    {
        var half = (float)(field.WidthAt(z) / 2);
        var height = (float)z;
        return [new(-half, -half, height), new(half, -half, height),
            new(half, half, height), new(-half, half, height)];
    }

    private static LineGeometry3D CreateOutline(EngravingField field, double z)
    {
        var lines = new LineBuilder();
        var corners = Corners(field, z);
        for (var i = 0; i < corners.Length; i++)
            lines.AddLine(corners[i], corners[(i + 1) % corners.Length]);
        return lines.ToLineGeometry3D();
    }

    private static MeshGeometry3D? CreateBand(EngravingField field, double bottom, double top)
    {
        if (top - bottom < 0.0001)
            return null;

        var positions = new Vector3Collection(24);
        var normals = new Vector3Collection(24);
        var indices = new IntCollection(24);
        var lower = Corners(field, bottom);
        var upper = Corners(field, top);
        for (var i = 0; i < lower.Length; i++)
        {
            var next = (i + 1) % lower.Length;
            AddTriangle(lower[i], lower[next], upper[i]);
            if (top < field.Height)
                AddTriangle(lower[next], upper[next], upper[i]);
        }

        return new MeshGeometry3D { Positions = positions, Normals = normals, Indices = indices };

        void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            foreach (var point in new[] { a, b, c })
            {
                indices.Add(positions.Count);
                positions.Add(point);
                normals.Add(normal);
            }
        }
    }

    /// <summary>Builds separate line sets for engraving and non-emitting movements on a worker thread.</summary>
    public static Task<(LineGeometry3D Travel, LineGeometry3D Burn)> CreateToolpathAsync(Toolpath toolpath, CancellationToken token)
        => Task.Run(() =>
        {
            var travel = new LineBuilder();
            var burn = new LineBuilder();
            foreach (var segment in toolpath.Segments)
            {
                token.ThrowIfCancellationRequested();
                var builder = segment.Laser == LaserMode.Off ? travel : burn;
                builder.AddLine(segment.Start, segment.End);
            }
            return (travel.ToLineGeometry3D(), burn.ToLineGeometry3D());
        }, token);
}
