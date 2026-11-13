using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using LaserMesh.Rendering;
using Vector3 = System.Numerics.Vector3;

namespace LaserMesh.Studio;

public partial class MainWindow
{
    private readonly DispatcherTimer playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch playbackClock = new();
    private readonly TranslateTransform3D markerPosition = new();
    private Rect3D toolpathBounds = Rect3D.Empty;
    private bool ticking;
    private Point? menuPress;

    private void InitializeEngraving()
    {
        FieldSides.Material = Material(new Color4(0.55f, 0.38f, 0.88f, 0.04f));
        FieldBelow.Material = Material(new Color4(1f, 0.58f, 0.17f, 0.085f));
        PlacedToolpath.Transform = pathPlacement;
        var marker = new LineBuilder();
        marker.AddLine(-Vector3.UnitX, Vector3.UnitX);
        marker.AddLine(-Vector3.UnitY, Vector3.UnitY);
        marker.AddLine(-Vector3.UnitZ, Vector3.UnitZ);
        ToolMarker.Geometry = marker.ToLineGeometry3D();
        ToolMarker.Transform = markerPosition;
        playbackTimer.Tick += OnPlaybackTick;
        viewModel.FieldChanged += SyncField;
        viewModel.ToolpathChanged += SyncToolpath;
        viewModel.MoveRequested += FocusMove;
        SyncField();
        SyncToolpath();
        FitScene();
    }

    private void ReleaseEngraving()
    {
        PausePlayback();
        playbackTimer.Tick -= OnPlaybackTick;
        viewModel.FieldChanged -= SyncField;
        viewModel.ToolpathChanged -= SyncToolpath;
        viewModel.MoveRequested -= FocusMove;
    }

    private void SyncField()
    {
        var (above, below, outline, section) = EngravingGeometry.CreateField(viewModel.Field, referenceZ);
        FieldSides.Geometry = above;
        FieldBelow.Geometry = below;
        FieldOutline.Geometry = outline;
        FieldSection.Geometry = section;
        FieldSides.Visibility = FieldOutline.Visibility = viewModel.ShowField ? Visibility.Visible : Visibility.Collapsed;
        FieldBelow.Visibility = FieldSection.Visibility = FieldSides.Visibility;
        RefreshFaceSummary();
        UpdateEmptyScene();
        if (visuals.Count == 0 && viewModel.Toolpath is null)
            FitScene();
    }

    private void OnFitField(object sender, RoutedEventArgs e) => FitScene(fieldOnly: true);

    private void SyncToolpath()
    {
        PausePlayback();
        TravelPath.Geometry = viewModel.TravelGeometry;
        BurnPath.Geometry = viewModel.BurnGeometry;
        toolpathBounds = Rect3D.Empty;
        foreach (var geometry in new[] { viewModel.TravelGeometry, viewModel.BurnGeometry })
        {
            if (geometry is { Positions.Count: > 0 })
            {
                var box = geometry.Bound;
                var size = box.Maximum - box.Minimum;
                toolpathBounds.Union(new Rect3D(box.Minimum.X, box.Minimum.Y, box.Minimum.Z, size.X, size.Y, size.Z));
            }
        }
        PlaybackSlider.Maximum = viewModel.Toolpath?.DurationSeconds ?? 1;
        PlaybackSlider.Value = 0;
        SetAlignmentGuide(false);
        SyncMarker();
        UpdateEmptyScene();
    }

    private void UpdateEmptyScene()
        => EmptyScene.Visibility = visuals.Count == 0 && viewModel.Toolpath is null && !viewModel.ShowField
            ? Visibility.Visible : Visibility.Collapsed;

    private void OnTogglePlayback(object sender, RoutedEventArgs e)
    {
        if (viewModel.Toolpath is null)
            return;
        if (playbackTimer.IsEnabled)
        {
            PausePlayback();
            return;
        }

        if (PlaybackSlider.Value >= PlaybackSlider.Maximum)
            PlaybackSlider.Value = 0;
        playbackClock.Restart();
        playbackTimer.Start();
        PlayButton.Content = "Pausar";
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        var next = PlaybackSlider.Value + playbackClock.Elapsed.TotalSeconds;
        playbackClock.Restart();
        ticking = true;
        PlaybackSlider.Value = Math.Min(next, PlaybackSlider.Maximum);
        ticking = false;
        if (next >= PlaybackSlider.Maximum)
            PausePlayback();
    }

    private void OnPlaybackPosition(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ticking && playbackTimer.IsEnabled)
            playbackClock.Restart();
        if (ToolMarker is not null)
            SyncMarker();
    }

    private void SyncMarker()
    {
        if (viewModel.Toolpath is not { } toolpath)
        {
            ToolMarker.Visibility = Visibility.Collapsed;
            return;
        }
        var point = toolpath.PositionAt(PlaybackSlider.Value);
        markerPosition.OffsetX = point.X;
        markerPosition.OffsetY = point.Y;
        markerPosition.OffsetZ = point.Z;
        ToolMarker.Visibility = Visibility.Visible;
    }

    private void PausePlayback()
    {
        playbackTimer.Stop();
        playbackClock.Stop();
        PlayButton.Content = "Reproducir";
    }

    private void OnObjectMenuDown(object sender, MouseButtonEventArgs e)
        => menuPress = Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control ? e.GetPosition(Viewport) : null;

    private void OnObjectMenuMove(object sender, MouseEventArgs e)
    {
        ContinuePointerEdit(e);
        if (menuPress is { } start && IsDrag(start, e.GetPosition(Viewport)))
            menuPress = null;
    }

    private void OnObjectMenuUp(object sender, MouseButtonEventArgs e)
    {
        var start = menuPress;
        menuPress = null;
        var point = e.GetPosition(Viewport);
        if (viewModel.IsBusy || start is null || IsDrag(start.Value, point)) return;
        var selected = Pick(point, out _);
        if (selected is not null && !viewModel.Selection.Ids.Contains(selected.Model.Id))
            viewModel.SelectHit(selected, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        var menu = (ContextMenu)Viewport.Resources[selected is null ? "BackgroundMenu" : "ObjectMenu"];
        menu.PlacementTarget = Viewport;
        menu.DataContext = viewModel;
        // Let Helix release the orbit gesture's mouse capture first.
        Dispatcher.BeginInvoke(() => menu.IsOpen = true, DispatcherPriority.Input);
    }

    private static bool IsDrag(Point start, Point current)
        => Math.Abs(start.X - current.X) >= SystemParameters.MinimumHorizontalDragDistance
           || Math.Abs(start.Y - current.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private void FocusMove()
    {
        MoveX.BringIntoView();
        MoveX.Focus();
        MoveX.SelectAll();
    }
}
