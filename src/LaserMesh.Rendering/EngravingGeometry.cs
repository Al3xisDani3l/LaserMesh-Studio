using System.Numerics;
using HelixToolkit;
using HelixToolkit.SharpDX;
using LaserMesh.Engraving;

namespace LaserMesh.Rendering;

/// <summary>Converts engraving overlays to renderer geometry.</summary>
public static class EngravingGeometry
{
    /// <summary>Builds the four open sides of the estimated field and a base outline.</summary>
    public static (MeshGeometry3D Sides, LineGeometry3D Outline) CreateField(EngravingField field)
    {
        var half = (float)(field.PlaneWidth / 2);
        var top = new Vector3(0, 0, (float)field.Height);
        Vector3[] basePoints = [new(-half, -half, 0), new(half, -half, 0), new(half, half, 0), new(-half, half, 0)];
        var positions = new Vector3Collection(12);
        var normals = new Vector3Collection(12);
        var indices = new IntCollection(12);
        var lines = new LineBuilder();
        for (var i = 0; i < basePoints.Length; i++)
        {
            var a = basePoints[i];
            var b = basePoints[(i + 1) % basePoints.Length];
            var normal = Vector3.Normalize(Vector3.Cross(b - a, top - a));
            foreach (var point in new[] { a, b, top })
            {
                indices.Add(positions.Count);
                positions.Add(point);
                normals.Add(normal);
            }
            lines.AddLine(a, b);
        }

        return (new MeshGeometry3D { Positions = positions, Normals = normals, Indices = indices },
            lines.ToLineGeometry3D());
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
