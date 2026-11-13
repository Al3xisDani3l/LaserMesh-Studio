using System.Numerics;
using LaserMesh.Geometry;
using LaserMesh.Projects;

namespace LaserMesh.Imaging;

/// <summary>Decoded RGBA image in row-major order.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Rgba">Four bytes per pixel, RGBA.</param>
public sealed record ImagePixels(int Width, int Height, byte[] Rgba);

/// <summary>Top-down composite with physical scene bounds and independent surface height range.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Gray">Composited grayscale pixels.</param>
/// <param name="Minimum">Scene XY minimum.</param>
/// <param name="Maximum">Scene XY maximum.</param>
/// <param name="MinimumZ">Lowest visible triangle height.</param>
/// <param name="MaximumZ">Highest visible triangle height.</param>
public sealed record ProjectedImage(int Width, int Height, byte[] Gray, Vector2 Minimum, Vector2 Maximum, float MinimumZ, float MaximumZ);

/// <summary>Resolves scene occlusion before sampling planar, cylindrical or UV-assigned designs.</summary>
public static class SurfaceCompositor
{
    /// <summary>Builds a fixed-head XY raster from visible scene surfaces; does not assume dynamic focus.</summary>
    public static ProjectedImage Compose(SceneDocument document, IReadOnlyDictionary<Guid, ImagePixels> images,
        double pixelsPerMm = 10, CancellationToken cancellationToken = default, Matrix4x4? registration = null)
    {
        if (!double.IsFinite(pixelsPerMm) || pixelsPerMm is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(pixelsPerMm));
        var models = document.Models.Where(m => m.Visible).ToArray();
        var points = models.SelectMany(m => m.Mesh.Positions.ToArray().Select(p => Vector3.Transform(p, m.Pose.Matrix * (registration ?? Matrix4x4.Identity)))).ToArray();
        if (points.Length == 0 || document.Designs.Count == 0) throw new InvalidDataException("Añade un diseño a una pieza visible.");
        var min3 = points.Aggregate(Vector3.Min); var max3 = points.Aggregate(Vector3.Max);
        var min = new Vector2(min3.X, min3.Y); var max = new Vector2(max3.X, max3.Y);
        var rawWidth = Math.Ceiling((max.X - min.X) * pixelsPerMm);
        var rawHeight = Math.Ceiling((max.Y - min.Y) * pixelsPerMm);
        if (!double.IsFinite(rawWidth) || !double.IsFinite(rawHeight) || rawWidth > 8192 || rawHeight > 8192 || rawWidth * rawHeight > 16_777_216)
            throw new InvalidDataException("La composición excede el límite de 16 megapíxeles.");
        var width = Math.Max(1, (int)rawWidth); var height = Math.Max(1, (int)rawHeight);
        var gray = Enumerable.Repeat((byte)255, width * height).ToArray();
        var depth = Enumerable.Repeat(float.NegativeInfinity, gray.Length).ToArray();
        foreach (var model in models)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var local = model.Mesh.Positions.ToArray();
            var world = local.Select(p => Vector3.Transform(p, model.Pose.Matrix * (registration ?? Matrix4x4.Identity))).ToArray();
            var designs = document.Designs.Where(d => d.ModelId == model.Id).Select(d =>
            {
                UvAtlas? atlas = d.AtlasId is { } id ? UvAtlas.Decode(document.Assets.Single(a => a.Id == id).Content) : null;
                if (!images.TryGetValue(d.AssetId, out var pixels) || pixels.Width <= 0 || pixels.Height <= 0
                    || pixels.Rgba.Length != (long)pixels.Width * pixels.Height * 4) throw new InvalidDataException("Imagen sin decodificar.");
                return (Design: d, Uv: SurfaceMapping.Coordinates(model.Mesh, d, atlas), Image: pixels);
            }).ToArray();
            for (var i = 0; i < world.Length; i += 3)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var a = world[i]; var b = world[i + 1]; var c = world[i + 2];
                var ab = new Vector2(b.X - a.X, b.Y - a.Y); var ac = new Vector2(c.X - a.X, c.Y - a.Y);
                var area = ab.X * ac.Y - ab.Y * ac.X;
                if (Math.Abs(area) < 1e-10) continue;
                var x0 = Math.Clamp((int)Math.Floor((Math.Min(a.X, Math.Min(b.X, c.X)) - min.X) * pixelsPerMm), 0, width - 1);
                var x1 = Math.Clamp((int)Math.Ceiling((Math.Max(a.X, Math.Max(b.X, c.X)) - min.X) * pixelsPerMm), 0, width - 1);
                var y0 = Math.Clamp((int)Math.Floor((max.Y - Math.Max(a.Y, Math.Max(b.Y, c.Y))) * pixelsPerMm), 0, height - 1);
                var y1 = Math.Clamp((int)Math.Ceiling((max.Y - Math.Min(a.Y, Math.Min(b.Y, c.Y))) * pixelsPerMm), 0, height - 1);
                for (var y = y0; y <= y1; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var x = x0; x <= x1; x++)
                    {
                        var p = new Vector2((float)(min.X + (x + 0.5) / pixelsPerMm - a.X), (float)(max.Y - (y + 0.5) / pixelsPerMm - a.Y));
                        var v = (p.X * ac.Y - p.Y * ac.X) / area; var w = (ab.X * p.Y - ab.Y * p.X) / area; var u = 1 - v - w;
                        if (u < -1e-6 || v < -1e-6 || w < -1e-6) continue;
                        var z = u * a.Z + v * b.Z + w * c.Z; var index = y * width + x;
                        if (z < depth[index] - 0.00001f) continue;
                        depth[index] = z; double value = 255;
                        foreach (var (design, uv, image) in designs)
                        {
                            var normal = model.Mesh.FaceNormals[i / 3];
                            var facing = Vector3.Dot(normal, Vector3.Normalize(design.Normal));
                            if (design.Projection == DesignProjection.Planar && facing <= 0.001
                                || design.Projection == DesignProjection.Cylindrical && Math.Abs(facing) >= 0.95) continue;
                            var coord = uv[i] * u + uv[i + 1] * v + uv[i + 2] * w;
                            if (coord.X < design.Crop.X || coord.Y < design.Crop.Y || coord.X > design.Crop.Z || coord.Y > design.Crop.W) continue;
                            var sx = Math.Clamp((int)(coord.X * image.Width), 0, image.Width - 1);
                            var sy = Math.Clamp((int)(coord.Y * image.Height), 0, image.Height - 1);
                            var at = (sy * image.Width + sx) * 4;
                            var sample = (image.Rgba[at] * 299 + image.Rgba[at + 1] * 587 + image.Rgba[at + 2] * 114 + 500) / 1000;
                            var alpha = image.Rgba[at + 3] / 255d * design.Opacity;
                            value = value * (1 - alpha) + sample * alpha;
                        }
                        gray[index] = (byte)Math.Clamp(Math.Round(value), 0, 255);
                    }
                }
            }
        }
        var visibleHeights = depth.Where(float.IsFinite).ToArray();
        if (visibleHeights.Length == 0) throw new InvalidDataException("La escena no tiene superficies visibles desde el láser.");
        return new(width, height, gray, min, max, visibleHeights.Min(), visibleHeights.Max());
    }
}
