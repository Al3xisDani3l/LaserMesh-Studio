using System.Numerics;
using LaserMesh.Geometry;
using LaserMesh.Projects;

namespace LaserMesh.Imaging;

/// <summary>Physical planar and cylindrical texture coordinates for object-local designs.</summary>
public static class SurfaceMapping
{
    /// <summary>Returns normalized image coordinates; points outside [0,1] are clipped by the compositor.</summary>
    public static Vector2 Map(Vector3 point, SurfaceDesign design, double radius = 1)
    {
        if (!double.IsFinite(design.Width) || !double.IsFinite(design.Height) || design.Width <= 0 || design.Height <= 0
            || !float.IsFinite(design.Normal.LengthSquared()) || design.Normal.LengthSquared() < 0.0001f
            || !double.IsFinite(radius) || radius <= 0 || !double.IsFinite(design.Angle)
            || design.Projection == DesignProjection.Cylindrical && (!double.IsFinite(design.CylinderRadius) || design.CylinderRadius <= 0 || !double.IsFinite(design.SeamDegrees)))
            throw new ArgumentException("Parámetros de superficie inválidos.");
        var normal = Vector3.Normalize(design.Normal);
        var u = Vector3.Normalize(Vector3.Cross(Math.Abs(normal.Z) > 0.9f ? Vector3.UnitY : Vector3.UnitZ, normal));
        var v = Vector3.Cross(normal, u); var local = point - design.Origin;
        double x, y;
        if (design.Projection == DesignProjection.Cylindrical)
        {
            var angle = Math.Atan2(Vector3.Dot(local, v), Vector3.Dot(local, u)) - design.SeamDegrees * Math.PI / 180;
            angle = Math.Atan2(Math.Sin(angle), Math.Cos(angle));
            x = angle * design.CylinderRadius; y = Vector3.Dot(local, normal);
        }
        else { x = Vector3.Dot(local, u); y = Vector3.Dot(local, v); }
        var radians = -design.Angle * Math.PI / 180;
        return new((float)((x * Math.Cos(radians) - y * Math.Sin(radians)) / design.Width + 0.5),
            (float)(0.5 - (x * Math.Sin(radians) + y * Math.Cos(radians)) / design.Height));
    }

    /// <summary>Builds one UV per triangle corner, retaining original geometry and atlas seams.</summary>
    public static Vector2[] Coordinates(TriangleMesh mesh, SurfaceDesign design, UvAtlas? atlas = null, double radius = 1)
    {
        if (design.Projection == DesignProjection.Uv)
        {
            if (atlas is null || atlas.Coordinates.Length != mesh.Positions.Length)
                throw new InvalidDataException("El diseño necesita un atlas compatible con su malla.");
            var output = atlas.Coordinates.ToArray();
            foreach (var placement in design.Islands)
            {
                var indices = Enumerable.Range(0, output.Length).Where(i => atlas.Charts[i] == placement.Chart).ToArray();
                if (indices.Length == 0) continue;
                var min = indices.Select(i => atlas.Coordinates[i]).Aggregate(Vector2.Min);
                var max = indices.Select(i => atlas.Coordinates[i]).Aggregate(Vector2.Max);
                var center = (min + max) / 2;
                var matrix = Matrix3x2.CreateTranslation(-center) * Matrix3x2.CreateScale((float)placement.Scale)
                    * Matrix3x2.CreateRotation((float)(placement.Angle * Math.PI / 180))
                    * Matrix3x2.CreateTranslation(center + placement.Offset);
                foreach (var index in indices) output[index] = Vector2.Transform(output[index], matrix);
            }
            return output;
        }
        return mesh.Positions.ToArray().Select(p => Map(p, design, radius)).ToArray();
    }
}
