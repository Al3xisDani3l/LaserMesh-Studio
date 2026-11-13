using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using LaserMesh.Geometry;
using LaserMesh.Imaging;
using LaserMesh.Projects;

namespace LaserMesh.Studio;

public partial class MainWindow
{
    private CancellationTokenSource? designRenderWork;
    private readonly List<(Guid Id, MeshGeometryModel3D Model, MemoryStream Stream)> designVisuals = [];
    private SurfaceDesign[] renderedDesigns = [];
    private readonly Dictionary<Guid, TriangleMesh> renderedMeshes = [];

    private async void SyncDesigns()
    {
        var current = viewModel.CaptureDocument();
        if (designRenderWork is null && renderedDesigns.SequenceEqual(current.Designs)
            && current.Designs.All(d => current.Models.Any(m => m.Id == d.ModelId && renderedMeshes.GetValueOrDefault(m.Id) == m.Mesh)))
        {
            foreach (var (id, visual, _) in designVisuals)
            {
                var design = current.Designs.FirstOrDefault(d => d.Id == id);
                var model = current.Models.FirstOrDefault(m => m.Id == design?.ModelId);
                visual.Visibility = model is { Visible: true } ? Visibility.Visible : Visibility.Hidden;
                if (model is not null) visual.Transform = Transform(model.Pose);
            }
            if (gestureStart is null) DrawAtlas();
            return;
        }
        designRenderWork?.Cancel();
        using var work = new CancellationTokenSource(); designRenderWork = work;
        var snapshot = viewModel.CaptureDocument();
        try
        {
            var prepared = await Task.Run(() => snapshot.Designs.Select(design =>
            {
                work.Token.ThrowIfCancellationRequested();
                var model = snapshot.Models.FirstOrDefault(m => m.Id == design.ModelId && m.Visible);
                if (model is null) return null;
                var asset = snapshot.Assets.Single(a => a.Id == design.AssetId);
                var atlas = design.AtlasId is { } id ? UvAtlas.Decode(snapshot.Assets.Single(a => a.Id == id).Content) : null;
                var coordinates = SurfaceMapping.Coordinates(model.Mesh, design, atlas);
                var positions = model.Mesh.Positions.ToArray();
                var normals = model.Mesh.FaceNormals.ToArray();
                var visibleIndices = Enumerable.Range(0, normals.Length).Where(i => design.Projection == DesignProjection.Uv
                    || (design.Projection == DesignProjection.Cylindrical ? Math.Abs(Vector3.Dot(normals[i], Vector3.Normalize(design.Normal))) < 0.95f
                        : Vector3.Dot(normals[i], Vector3.Normalize(design.Normal)) > 0.001f)).SelectMany(i => new[] { i * 3, i * 3 + 1, i * 3 + 2 });
                var geometry = new MeshGeometry3D
                {
                    Positions = new Vector3Collection(positions), Indices = new IntCollection(visibleIndices),
                    Normals = new Vector3Collection(normals.SelectMany(n => new[] { n, n, n })),
                    TextureCoordinates = new Vector2Collection(coordinates)
                };
                var pixels = StudioViewModel.DecodeImage(asset.Content);
                for (var y = 0; y < pixels.Height; y++)
                {
                    work.Token.ThrowIfCancellationRequested();
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        var at = (y * pixels.Width + x) * 4;
                        (pixels.Rgba[at], pixels.Rgba[at + 2]) = (pixels.Rgba[at + 2], pixels.Rgba[at]);
                        var u = (x + 0.5) / pixels.Width; var v = (y + 0.5) / pixels.Height;
                        if (u < design.Crop.X || v < design.Crop.Y || u > design.Crop.Z || v > design.Crop.W) pixels.Rgba[at + 3] = 0;
                    }
                }
                var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null, pixels.Rgba, pixels.Width * 4);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var encoded = new MemoryStream(); encoder.Save(encoded);
                return new DesignRender(design, model.Pose, geometry, encoded.ToArray());
            }).Where(x => x is not null).ToArray(), work.Token);
            if (work != designRenderWork) return;
            ClearDesignVisuals();
            foreach (var entry in prepared)
            {
                var stream = new MemoryStream(entry!.Content, writable: false);
                var material = new PhongMaterial { DiffuseMap = TextureModel.Create(stream),
                    DiffuseColor = new Color4(1, 1, 1, (float)entry.Design.Opacity),
                    DiffuseMapSampler = new SharpDX.Direct3D11.SamplerStateDescription
                    {
                        Filter = SharpDX.Direct3D11.Filter.MinMagMipLinear,
                        AddressU = SharpDX.Direct3D11.TextureAddressMode.Border,
                        AddressV = SharpDX.Direct3D11.TextureAddressMode.Border,
                        AddressW = SharpDX.Direct3D11.TextureAddressMode.Border,
                        BorderColor = new(0, 0, 0, 0), MaximumLod = float.MaxValue
                    }
                };
                var visual = new MeshGeometryModel3D { Geometry = entry.Geometry, Material = material,
                    Transform = Transform(entry.Pose), IsTransparent = true, IsHitTestVisible = false, DepthBias = -200 };
                DesignScene.Children.Add(visual); designVisuals.Add((entry.Design.Id, visual, stream));
            }
            renderedDesigns = snapshot.Designs.ToArray(); renderedMeshes.Clear();
            foreach (var model in snapshot.Models) renderedMeshes[model.Id] = model.Mesh;
            DrawAtlas();
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        { ToolSummary.Text = $"Diseño: {ex.Message}"; }
        finally { if (designRenderWork == work) designRenderWork = null; }
    }

    private void ClearDesignVisuals()
    {
        DesignScene.Children.Clear();
        foreach (var (_, model, stream) in designVisuals) { model.Dispose(); stream.Dispose(); }
        designVisuals.Clear();
    }

    private void DrawAtlas()
    {
        AtlasCanvas.Children.Clear();
        if (viewModel.SelectedDesign is not { AtlasId: { } id } selected) return;
        var doc = viewModel.CaptureDocument(); var atlas = UvAtlas.Decode(doc.Assets.Single(a => a.Id == id).Content);
        var model = doc.Models.Single(m => m.Id == selected.ModelId);
        var coordinates = SurfaceMapping.Coordinates(model.Mesh, selected, atlas);
        var chosen = int.TryParse(viewModel.SelectedChart, out var chart) ? chart : -1;
        var positions = model.Mesh.Positions.ToArray();
        var densities = new double[coordinates.Length / 3];
        for (var i = 0; i < coordinates.Length; i += 3)
        {
            var a = coordinates[i + 1] - coordinates[i]; var b = coordinates[i + 2] - coordinates[i];
            var area = Vector3.Cross(positions[i + 1] - positions[i], positions[i + 2] - positions[i]).Length();
            densities[i / 3] = area > 1e-12 ? Math.Abs(a.X * b.Y - a.Y * b.X) / area : 0;
        }
        var positive = densities.Where(v => v > 0).Order().ToArray(); var median = positive.Length > 0 ? positive[positive.Length / 2] : 1;
        // The atlas window is a bounded preview; full data remains in the project even for large meshes.
        var stride = Math.Max(1, coordinates.Length / (3 * 2000));
        for (var i = 0; i < coordinates.Length; i += 3 * stride)
        {
            var index = i;
            var polygon = new Polygon { Stroke = atlas.Charts[i] == chosen ? Brushes.Gold : Brushes.LightSkyBlue,
                StrokeThickness = 0.7, Fill = atlas.Charts[i] == chosen ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 253, 216, 0)) : Brushes.Transparent };
            if (viewModel.ShowUvDistortion)
            {
                var ratio = densities[i / 3] / median;
                polygon.Fill = ratio is > 2 or < 0.5 ? Brushes.OrangeRed : ratio is > 1.25 or < 0.8 ? Brushes.Orange : Brushes.DarkSlateBlue;
                polygon.ToolTip = $"Isla {atlas.Charts[i]} · densidad relativa {ratio:0.###}";
            }
            for (var j = 0; j < 3; j++) polygon.Points.Add(new Point(coordinates[i + j].X * 240, coordinates[i + j].Y * 180));
            polygon.MouseLeftButtonDown += (_, e) => { viewModel.SelectedChart = atlas.Charts[index].ToString(); DrawAtlas(); e.Handled = true; };
            AtlasCanvas.Children.Add(polygon);
        }
    }
    private sealed record DesignRender(SurfaceDesign Design, ModelPose Pose, MeshGeometry3D Geometry, byte[] Content);
}
