using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using LaserMesh.Projects;
using LaserMesh.Rendering;
using WpfOrthographicCamera = HelixToolkit.Wpf.SharpDX.OrthographicCamera;
using WpfPerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using Vector3 = System.Numerics.Vector3;

namespace LaserMesh.Studio;

/// <summary>Studio workspace with a DirectX viewport and contextual inspector.</summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly StudioViewModel viewModel;
    private readonly MeshAdapter meshes;
    private readonly DefaultEffectsManager effects;
    private readonly Dictionary<Guid, MeshGeometryModel3D> visuals = [];
    private readonly PhongMaterial standard = Material(new Color4(0.56f, 0.62f, 0.72f, 1));
    private readonly PhongMaterial highlighted = Material(new Color4(0.36f, 0.83f, 0.62f, 1));
    private static readonly Vector3D IsometricDirection = new(-1, 1, -0.8);
    private bool allowClose;
    private bool closing;
    private float gridSpacing;

    internal MainWindow(StudioViewModel viewModel, MeshAdapter meshes)
    {
        this.viewModel = viewModel;
        this.meshes = meshes;
        InitializeComponent();
        DataContext = viewModel;

        effects = new DefaultEffectsManager();
        Viewport.EffectsManager = effects;
        SetCamera(false, IsometricDirection, new(0, 0, 1), "PERSPECTIVA");
        UpdateGrid(10);
        viewModel.SceneChanged += SyncScene;
        viewModel.PropertyChanged += OnViewModelChanged;
        InitializeEngraving();
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void SyncScene(bool fit)
    {
        var ids = viewModel.Models.Select(x => x.Model.Id).ToHashSet();
        foreach (var id in visuals.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            Scene.Children.Remove(visuals[id]);
            visuals[id].Dispose();
            visuals.Remove(id);
        }

        foreach (var item in viewModel.Models)
        {
            var model = item.Model;
            if (!visuals.TryGetValue(model.Id, out var element))
            {
                element = new MeshGeometryModel3D { Geometry = item.Geometry, Material = standard };
                visuals.Add(model.Id, element);
                Scene.Children.Add(element);
            }
            if (element.Geometry != item.Geometry)
                element.Geometry = item.Geometry;
            element.Transform = Transform(model.Pose);
        }

        UpdateEmptyScene();
        SyncSelection();
        ApplyWireframe();
        UpdateSurfaceReference();
        if (fit)
            FitScene();
    }

    private static Transform3D Transform(ModelPose pose)
    {
        var matrix = Matrix3D.Identity;
        matrix.Scale(new(pose.Scale, pose.Scale, pose.Scale));
        matrix.Rotate(new Quaternion(new Vector3D(0, 0, 1), pose.Rotation));
        matrix.Translate(new(pose.X, pose.Y, pose.Z));
        return new MatrixTransform3D(matrix);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudioViewModel.Selected))
            SyncSelection();
    }

    private void SyncSelection()
    {
        var selected = viewModel.Selected?.Model.Id;
        Inspector.Visibility = selected.HasValue ? Visibility.Visible : Visibility.Collapsed;
        EmptyInspector.Visibility = selected.HasValue ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (id, element) in visuals)
            element.Material = id == selected ? highlighted : standard;
    }

    private void OnPick(object sender, MouseButtonEventArgs e)
    {
        var item = Pick(e.GetPosition(Viewport), out var hit);
        viewModel.Selected = item;
        if (choosingFace && item is not null && hit is not null)
            SelectHitFace(item, hit);
    }

    private SceneItem? Pick(Point point, out HitTestResult? hit)
    {
        hit = Viewport.FindHits(point)?
            .FirstOrDefault(x => x.IsValid && x.ModelHit is MeshGeometryModel3D model && visuals.ContainsValue(model));
        var visual = hit?.ModelHit as MeshGeometryModel3D;
        return visual is null ? null
            : viewModel.Models.FirstOrDefault(x => visuals.GetValueOrDefault(x.Model.Id) == visual);
    }

    private void OnFit(object sender, RoutedEventArgs e) => FitScene();

    private async void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { DataContext: string path })
            await viewModel.OpenRecentAsync(path);
    }

    private void OnWireframe(object sender, RoutedEventArgs e) => ApplyWireframe();

    private void ApplyWireframe()
    {
        foreach (var model in visuals.Values)
            model.RenderWireframe = WireframeToggle.IsChecked == true;
    }

    private void OnPerspective(object sender, RoutedEventArgs e)
        => SetCamera(false, IsometricDirection, new(0, 0, 1), "PERSPECTIVA");

    private void OnOrthographic(object sender, RoutedEventArgs e)
        => SetCamera(true, IsometricDirection, new(0, 0, 1), "ORTOGRÁFICA · ISOMÉTRICA");

    private void OnTop(object sender, RoutedEventArgs e)
        => SetCamera(true, new(0, 0, -1), new(0, 1, 0), "ORTOGRÁFICA · SUPERIOR");

    private void OnFront(object sender, RoutedEventArgs e)
        => SetCamera(true, new(0, 1, 0), new(0, 0, 1), "ORTOGRÁFICA · FRONTAL");

    private void SetCamera(bool orthographic, Vector3D direction, Vector3D up, string label)
    {
        direction.Normalize();
        direction *= 240;
        HelixToolkit.Wpf.SharpDX.ProjectionCamera camera = orthographic
            ? new WpfOrthographicCamera { Width = 240 }
            : new WpfPerspectiveCamera { FieldOfView = 45 };
        camera.Position = new Point3D() - direction;
        camera.LookDirection = direction;
        camera.UpDirection = up;
        Viewport.Camera = camera;
        ProjectionLabel.Text = label;
        FitScene();
    }

    private void FitScene(bool fieldOnly = false)
    {
        var bounds = Rect3D.Empty;
        if (!fieldOnly)
        {
            foreach (var item in viewModel.Models)
            {
                var min = item.Model.Mesh.Minimum;
                var dimensions = item.Model.Mesh.Dimensions;
                var local = new Rect3D(min.X, min.Y, min.Z, dimensions.X, dimensions.Y, dimensions.Z);
                bounds.Union(Transform(item.Model.Pose).TransformBounds(local));
            }
            if (!toolpathBounds.IsEmpty)
                bounds.Union(pathPlacement.TransformBounds(toolpathBounds));
        }
        if (bounds.IsEmpty && viewModel.ShowField)
        {
            var width = viewModel.Field.PlaneWidth;
            bounds = new Rect3D(-width / 2, -width / 2, 0, width, width, viewModel.Field.Height);
        }

        if (bounds.IsEmpty)
        {
            UpdateGrid(10);
            return;
        }

        var size = new Vector3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ);
        var center = bounds.Location + size / 2;
        var radius = Math.Max(size.Length / 2, 1e-8);
        var aspect = Viewport.ActualWidth / Math.Max(Viewport.ActualHeight, 1);
        if (Viewport.Camera is HelixToolkit.Wpf.SharpDX.ProjectionCamera camera)
        {
            var direction = camera.LookDirection;
            direction.Normalize();
            var distance = radius * 3;
            if (camera is WpfPerspectiveCamera perspective)
            {
                var horizontalHalfAngle = perspective.FieldOfView * Math.PI / 360;
                var limitingHalfAngle = Math.Atan(Math.Tan(horizontalHalfAngle) / Math.Max(1, aspect));
                distance = 1.12 * radius / Math.Sin(limitingHalfAngle);
            }
            else if (camera is WpfOrthographicCamera orthographic)
            {
                orthographic.Width = radius * 2.24 * Math.Max(1, aspect);
            }

            camera.Position = center - direction * distance;
            camera.LookDirection = direction * distance;
            camera.NearPlaneDistance = Math.Max(radius / 10000, 1e-10);
            camera.FarPlaneDistance = distance + radius * 1000;
        }
        UpdateGrid((float)Math.Pow(10, Math.Floor(Math.Log10(radius / 2))));
    }

    private void UpdateGrid(float spacing)
    {
        if (spacing == gridSpacing)
            return;
        gridSpacing = spacing;
        GridLines.Geometry = meshes.CreateGrid(spacing);
        GridLabels.Geometry = meshes.CreateGridLabels(spacing);
        AxisX.Geometry = meshes.CreateAxis(Vector3.UnitX * spacing * 4);
        AxisY.Geometry = meshes.CreateAxis(Vector3.UnitY * spacing * 4);
        AxisZ.Geometry = meshes.CreateAxis(Vector3.UnitZ * spacing * 4);
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaserMeshStudio", "logs");
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Diagnóstico", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
            return;
        e.Cancel = true;
        if (closing)
            return;

        closing = true;
        try
        {
            if (await viewModel.ConfirmDiscardAsync())
            {
                allowClose = true;
                await Dispatcher.InvokeAsync(Close);
            }
        }
        finally
        {
            closing = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ReleaseEngraving();
        viewModel.SceneChanged -= SyncScene;
        viewModel.PropertyChanged -= OnViewModelChanged;
        Scene.Children.Clear();
        foreach (var visual in visuals.Values)
            visual.Dispose();
        Viewport.Dispose();
        effects.Dispose();
    }

    private static PhongMaterial Material(Color4 diffuse) => new()
    {
        DiffuseColor = diffuse,
        AmbientColor = new Color4(0.28f, 0.30f, 0.34f, 1),
        SpecularColor = new Color4(0.19f, 0.21f, 0.23f, 1),
        SpecularShininess = 24
    };
}
