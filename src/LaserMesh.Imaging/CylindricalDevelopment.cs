using System.Numerics;
using LaserMesh.Geometry;
using LaserMesh.Projects;

namespace LaserMesh.Imaging;

/// <summary>Develops a mesh around an explicitly defined cylindrical axis, preserving assigned texture coordinates.</summary>
public static class CylindricalDevelopment
{
    private readonly record struct Vertex(Vector3 Position, Vector2 Uv);

    /// <summary>Produces a surface-distance raster for simulation/export; does not encode accessory commands.</summary>
    public static ProjectedImage Compose(SceneDocument document, Guid designId, IReadOnlyDictionary<Guid, ImagePixels> images,
        CancellationToken cancellationToken = default)
    {
        var design = document.Designs.Single(d => d.Id == designId);
        var model = document.Models.Single(m => m.Id == design.ModelId);
        var axis = Vector3.Normalize(design.Normal);
        var u = Vector3.Normalize(Vector3.Cross(Math.Abs(axis.Z) > 0.9f ? Vector3.UnitY : Vector3.UnitZ, axis));
        var v = Vector3.Cross(axis, u);
        var radius = design.CylinderRadius * model.Pose.Scale;
        if (!double.IsFinite(radius) || radius <= 0) throw new InvalidDataException("Radio cilíndrico inválido.");
        var circumference = (float)(2 * Math.PI * radius); var half = circumference / 2;
        UvAtlas? atlas = design.AtlasId is { } id ? UvAtlas.Decode(document.Assets.Single(a => a.Id == id).Content) : null;
        var sourceUv = SurfaceMapping.Coordinates(model.Mesh, design, atlas);
        var positions = model.Mesh.Positions.ToArray(); var output = new List<Vector3>(); var uv = new List<Vector2>();
        for (var i = 0; i < positions.Length; i += 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Math.Abs(Vector3.Dot(model.Mesh.FaceNormals[i / 3], axis)) > 0.95) continue;
            var triangle = new Vertex[3];
            for (var j = 0; j < 3; j++)
            {
                var local = positions[i + j] - design.Origin;
                var angle = Math.Atan2(Vector3.Dot(local, v), Vector3.Dot(local, u)) - design.SeamDegrees * Math.PI / 180;
                angle = Math.Atan2(Math.Sin(angle), Math.Cos(angle));
                var s = (float)(angle * radius);
                var originalS = s;
                if (j > 0)
                {
                    while (s - triangle[0].Position.X > half) s -= circumference;
                    while (s - triangle[0].Position.X < -half) s += circumference;
                }
                triangle[j] = new(new(s, (float)(Vector3.Dot(local, axis) * model.Pose.Scale), 0), sourceUv[i + j] + TextureShift(s - originalS));
            }
            foreach (var shift in new[] { -circumference, 0, circumference })
            {
                var polygon = triangle.Select(p => p with { Position = p.Position + new Vector3(shift, 0, 0), Uv = p.Uv + TextureShift(shift) }).ToList();
                polygon = Clip(polygon, -half, true); polygon = Clip(polygon, half, false);
                for (var j = 1; j + 1 < polygon.Count; j++)
                {
                    foreach (var vertex in new[] { polygon[0], polygon[j], polygon[j + 1] })
                    { output.Add(vertex.Position); uv.Add(vertex.Uv); }
                    if (output.Count > TriangleMesh.MaxTriangles * 3) throw new InvalidDataException("El desarrollo excede el límite de triángulos.");
                }
            }
        }
        if (output.Count == 0) throw new InvalidDataException("No hay caras cilíndricas desarrollables en ese eje.");
        var unrolled = TriangleMesh.FromTriangles(output, cancellationToken);
        var atlasId = Guid.NewGuid();
        var resource = new DesignAsset(atlasId, "Desarrollo temporal", "application/x-lasermesh-uv", new UvAtlas(uv.ToArray(), new int[uv.Count]).Encode());
        var mapped = design with { Projection = DesignProjection.Uv, AtlasId = atlasId, Islands = [] };
        var scene = new SceneDocument { Models = [model with { Mesh = unrolled, Pose = ModelPose.Identity }],
            Assets = [.. document.Assets, resource], Designs = [mapped] };
        return SurfaceCompositor.Compose(scene, images, 10, cancellationToken);
        Vector2 TextureShift(float distance)
        {
            if (design.Projection != DesignProjection.Cylindrical) return Vector2.Zero;
            var radians = -design.Angle * Math.PI / 180;
            return new((float)(distance / model.Pose.Scale * Math.Cos(radians) / design.Width),
                (float)(-distance / model.Pose.Scale * Math.Sin(radians) / design.Height));
        }
    }

    private static List<Vertex> Clip(List<Vertex> input, float edge, bool minimum)
    {
        var output = new List<Vertex>(); if (input.Count == 0) return output;
        var previous = input[^1]; var insidePrevious = minimum ? previous.Position.X >= edge : previous.Position.X <= edge;
        foreach (var current in input)
        {
            var inside = minimum ? current.Position.X >= edge : current.Position.X <= edge;
            if (inside != insidePrevious)
            {
                var t = (edge - previous.Position.X) / (current.Position.X - previous.Position.X);
                output.Add(new(Vector3.Lerp(previous.Position, current.Position, t), Vector2.Lerp(previous.Uv, current.Uv, t)));
            }
            if (inside) output.Add(current);
            previous = current; insidePrevious = inside;
        }
        return output;
    }
}
