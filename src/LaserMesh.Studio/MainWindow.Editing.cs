using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using Vector3 = System.Numerics.Vector3;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace LaserMesh.Studio;

public partial class MainWindow
{
    private bool synchronizingTree;
    private Point? gestureStart;
    private Point3D? gestureAnchor;
    private Vector3 gesturePivot;
    private Vector3D gesturePlane = new(0, 0, 1);
    private bool gestureMoving;
    private bool marquee;
    private string editTool = "XY";
    private Vector3? measureStart;
    private readonly Dictionary<string, Vector3> gizmoHandles = [];

    private void OnToolChoice(object sender, SelectionChangedEventArgs e)
    {
        if (EditToolPicker?.SelectedItem is ComboBoxItem { Tag: string tool })
        {
            editTool = tool;
            if (ToolSummary is not null) ToolSummary.Text = $"{tool} · arrastra selección · Esc cancela";
            if (IsLoaded) SyncGizmo();
        }
    }

    private void SyncEditingSelection()
    {
        if (ProjectTree is null) return;
        synchronizingTree = true;
        try
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ProjectTree.ItemsSource);
            if (view is { CanGroup: true } && view.GroupDescriptions.Count == 0)
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(SceneItem.GroupLabel)));
            ProjectTree.SelectedItems.Clear();
            foreach (var item in viewModel.SelectedItems) ProjectTree.SelectedItems.Add(item);
        }
        finally { synchronizingTree = false; }
        SyncSelection(); SyncGizmo();
        if (IsLoaded && gestureStart is null) RefreshPlacement();
    }

    private void OnTreeSelection(object sender, SelectionChangedEventArgs e)
    {
        if (synchronizingTree || DataContext is not StudioViewModel) return;
        if (e.RemovedItems.Cast<SceneItem>().Any(old => !viewModel.Models.Contains(old))) return;
        viewModel.SelectItems(ProjectTree.SelectedItems.Cast<SceneItem>().Select(m => m.Model.Id),
            (ProjectTree.SelectedItem as SceneItem)?.Model.Id);
    }

    private void OnMenuTool(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tool })
        {
            editTool = tool;
            foreach (ComboBoxItem option in EditToolPicker.Items) if ((string)option.Tag == tool) EditToolPicker.SelectedItem = option;
        }
    }

    private void OnEditTool(object sender, RoutedEventArgs e)
    {
        editTool = (string)((Button)sender).Tag;
        ToolSummary.Text = $"Herramienta: {editTool} · arrastra selección · Esc cancela";
        SyncGizmo();
    }

    private void BeginPointerEdit(MouseButtonEventArgs e)
    {
        if (viewModel.IsBusy) return;
        var point = e.GetPosition(Viewport);
        var handle = gizmoHandles.FirstOrDefault(h =>
        {
            var screen = Viewport.Project(new Point3D(h.Value.X, h.Value.Y, h.Value.Z));
            var pivot = viewModel.SelectionPivot;
            var center = Viewport.Project(new Point3D(pivot.X, pivot.Y, pivot.Z));
            return (screen - point).Length <= 12 && (screen - center).Length > 12;
        }).Key;
        var manipulatingHandle = handle is not null && editTool != "Medir";
        if (manipulatingHandle) editTool = handle!;
        var item = Pick(point, out var hit);
        if (choosingFace)
        {
            viewModel.SelectHit(item, false);
            if (item is not null && hit is not null) SelectHitFace(item, hit);
            e.Handled = true; return;
        }
        if (editTool == "Medir" && hit is not null)
        {
            var p = hit.PointHit;
            if (measureStart is { } start)
            {
                var delta = p - start;
                ToolSummary.Text = $"Distancia 3D {delta.Length():0.###} mm · XY {Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y):0.###} mm";
                var builder = new LineBuilder(); builder.AddLine(start, p); Measurement.Geometry = builder.ToLineGeometry3D();
                measureStart = null;
            }
            else { measureStart = p; ToolSummary.Text = "Medir: elige el segundo punto"; }
            e.Handled = true; return;
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            var menu = new ContextMenu();
            var candidates = Viewport.FindHits(point)?.Where(h => h.ModelHit is MeshGeometryModel3D)
                .Select(h => viewModel.Models.FirstOrDefault(m => visuals.GetValueOrDefault(m.Model.Id) == h.ModelHit))
                .Where(m => m is not null).Distinct().ToArray() ?? [];
            foreach (var candidate in candidates)
            {
                var option = new MenuItem { Header = candidate!.Name };
                option.Click += (_, _) => viewModel.SelectHit(candidate, false); menu.Items.Add(option);
            }
            menu.PlacementTarget = Viewport; menu.IsOpen = candidates.Length > 0; e.Handled = true; return;
        }
        var toggle = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!manipulatingHandle && (item is null || toggle || !viewModel.Selection.Ids.Contains(item.Model.Id))) viewModel.SelectHit(item, toggle);
        gestureStart = point; gestureMoving = false; marquee = item is null && !manipulatingHandle;
        gesturePivot = viewModel.SelectionPivot;
        gesturePlane = new(0, 0, 1);
        if (editTool is "X" or "Y" or "Z" && Viewport.Camera is HelixToolkit.Wpf.SharpDX.ProjectionCamera camera)
        {
            var axis = editTool == "X" ? new Vector3D(1, 0, 0) : editTool == "Y" ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
            var normal = camera.LookDirection - axis * Vector3D.DotProduct(camera.LookDirection, axis);
            if (normal.Length < 0.0001) { gestureStart = null; ToolSummary.Text = "Cambia de vista para mover sobre ese eje."; return; }
            normal.Normalize(); gesturePlane = normal;
        }
        if (editTool == "Diseño" && viewModel.SelectedDesign is { } design)
        {
            var target = viewModel.Models.FirstOrDefault(m => m.Model.Id == design.ModelId);
            if (target is null) { gestureStart = null; return; }
            gesturePivot = Vector3.Transform(design.Origin, target.Model.Pose.Matrix);
            var normal = Vector3.Normalize(Vector3.TransformNormal(design.Normal, target.Model.Pose.Matrix));
            gesturePlane = new(normal.X, normal.Y, normal.Z);
        }
        gestureAnchor = Viewport.UnProjectOnPlane(point, new Point3D(gesturePivot.X, gesturePivot.Y, gesturePivot.Z), gesturePlane);
        if (item is not null && item.Model.Locked) { gestureStart = null; return; }
        Viewport.CaptureMouse(); e.Handled = true;
    }

    private void ContinuePointerEdit(MouseEventArgs e)
    {
        if (gestureStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(Viewport);
        if (!gestureMoving && !IsDrag(start, point)) return;
        if (marquee)
        {
            SelectionRectangle.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRectangle, Math.Min(start.X, point.X)); Canvas.SetTop(SelectionRectangle, Math.Min(start.Y, point.Y));
            SelectionRectangle.Width = Math.Abs(point.X - start.X); SelectionRectangle.Height = Math.Abs(point.Y - start.Y);
            return;
        }
        if (!gestureMoving)
        {
            if (editTool == "Diseño") viewModel.BeginDesignGesture(); else viewModel.BeginGesture();
            gestureMoving = true;
        }
        var fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1f : 1;
        var dx = (float)(point.X - start.X) * fine; var dy = (float)(point.Y - start.Y) * fine;
        var delta = Matrix4x4.Identity;
        if (editTool.StartsWith("R", StringComparison.Ordinal))
        {
            var radians = dx * MathF.PI / 360;
            var axis = editTool == "RX" ? Vector3.UnitX : editTool == "RY" ? Vector3.UnitY : Vector3.UnitZ;
            delta = Matrix4x4.CreateTranslation(-gesturePivot) * Matrix4x4.CreateFromAxisAngle(axis, radians)
                * Matrix4x4.CreateTranslation(gesturePivot);
        }
        else if (editTool == "Escala")
            delta = Matrix4x4.CreateTranslation(-gesturePivot) * Matrix4x4.CreateScale(Math.Clamp(MathF.Exp(dx / 150), 0.01f, 100))
                * Matrix4x4.CreateTranslation(gesturePivot);
        else
        {
            var current = Viewport.UnProjectOnPlane(point, new Point3D(gesturePivot.X, gesturePivot.Y, gesturePivot.Z), gesturePlane);
            if (current is null || gestureAnchor is null) return;
            var difference = current.Value - gestureAnchor.Value;
            var translation = new Vector3((float)difference.X, (float)difference.Y, (float)difference.Z) * fine;
            if (editTool == "Diseño" && viewModel.SelectedDesign is { } selectedDesign)
            {
                var target = viewModel.Models.First(m => m.Model.Id == selectedDesign.ModelId);
                if (System.Numerics.Matrix4x4.Invert(target.Model.Pose.Matrix, out var inverse))
                    viewModel.UpdateDesignGesture(Vector3.TransformNormal(translation, inverse));
                e.Handled = true; return;
            }
            if (editTool == "X") translation = new(translation.X, 0, 0);
            if (editTool == "Y") translation = new(0, translation.Y, 0);
            if (editTool == "Z") translation = new(0, 0, translation.Z);
            if (SnapMove.IsChecked == true)
                translation = new(MathF.Round(translation.X), MathF.Round(translation.Y), MathF.Round(translation.Z));
            delta = Matrix4x4.CreateTranslation(translation);
        }
        viewModel.UpdateGesture(delta); e.Handled = true;
    }

    private void OnPointerEditUp(object sender, MouseButtonEventArgs e)
    {
        if (gestureStart is not { } start) return;
        if (marquee && IsDrag(start, e.GetPosition(Viewport)))
        {
            var rect = new Rect(start, e.GetPosition(Viewport));
            var contains = e.GetPosition(Viewport).X >= start.X;
            var ids = viewModel.Models.Where(m => m.Model.Visible && !m.Model.Locked).Where(m =>
            {
                var bounds = StudioViewModel.Bounds([m]);
                var corners = Enumerable.Range(0, 8).Select(i => Viewport.Project(new Point3D(
                    (i & 1) == 0 ? bounds.Min.X : bounds.Max.X, (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                    (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z))).ToArray();
                var screenBounds = new Rect(new Point(corners.Min(p => p.X), corners.Min(p => p.Y)),
                    new Point(corners.Max(p => p.X), corners.Max(p => p.Y)));
                if (!(contains ? corners.All(rect.Contains) : rect.IntersectsWith(screenBounds))) return false;
                if (SelectThrough.IsChecked == true) return true;
                var positions = m.Model.Mesh.Positions;
                var stride = Math.Max(1, m.Model.Mesh.TriangleCount / 16);
                for (var triangle = 0; triangle < m.Model.Mesh.TriangleCount; triangle += stride)
                {
                    var i = triangle * 3; var local = (positions[i] + positions[i + 1] + positions[i + 2]) / 3;
                    var center = Vector3.Transform(local, m.Model.Pose.Matrix);
                    var screen = Viewport.Project(new Point3D(center.X, center.Y, center.Z));
                    if (rect.Contains(screen) && Pick(screen, out _)?.Model.Id == m.Model.Id) return true;
                }
                return false;
            }).Select(m => m.Model.Id);
            viewModel.SelectItems(Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? viewModel.Selection.Ids.Concat(ids) : ids);
        }
        FinishPointer(false); e.Handled = true;
    }
    private void FinishPointer(bool cancel)
    {
        gestureStart = null; SelectionRectangle.Visibility = Visibility.Collapsed;
        if (editTool == "Diseño") viewModel.EndDesignGesture(cancel); else viewModel.EndGesture(cancel);
        gestureMoving = false;
        Viewport.ReleaseMouseCapture(); RefreshPlacement();
    }
    private void OnEditingCaptureLost(object sender, MouseEventArgs e) { if (gestureStart is not null) FinishPointer(true); }

    private void OnEditingKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (e.Key == Key.Escape) { if (gestureStart is not null) FinishPointer(true); else viewModel.SelectItems([]); e.Handled = true; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.A) viewModel.SelectAllCommand.Execute(null);
            else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) viewModel.RedoCommand.Execute(null);
            else if (e.Key == Key.Z) viewModel.UndoCommand.Execute(null);
            else if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) viewModel.SaveAsCommand.Execute(null);
            else if (e.Key == Key.S) viewModel.SaveCommand.Execute(null);
            else if (e.Key == Key.O) viewModel.OpenCommand.Execute(null);
            else if (e.Key == Key.Y) viewModel.RedoCommand.Execute(null);
            else if (e.Key == Key.D) viewModel.DuplicateCommand.Execute(null);
            else return;
            e.Handled = true; return;
        }
        if (e.Key == Key.Delete) { viewModel.RemoveCommand.Execute(null); e.Handled = true; }
        if (e.Key == Key.G) editTool = "XY";
        if (e.Key == Key.R) editTool = "RZ";
        if (e.Key == Key.S) editTool = "Escala";
        if (e.Key == Key.F) FitSelection();
        if (e.Key == Key.Home) FitScene();
    }

    private void SyncGizmo()
    {
        if (SelectionGizmo is null) return;
        gizmoHandles.Clear();
        var builder = new LineBuilder();
        foreach (var item in viewModel.SelectedItems)
        {
            var (min, max) = StudioViewModel.Bounds([item]);
            for (var i = 0; i < 8; i++)
            {
                var a = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
                foreach (var bit in new[] { 1, 2, 4 })
                    if ((i & bit) == 0)
                    { var j = i | bit; builder.AddLine(a, new Vector3((j & 1) == 0 ? min.X : max.X, (j & 2) == 0 ? min.Y : max.Y, (j & 4) == 0 ? min.Z : max.Z)); }
            }
        }
        SelectionGizmo.Geometry = builder.ToLineGeometry3D();
        var center = viewModel.SelectionPivot;
        var bounds = StudioViewModel.Bounds(viewModel.SelectedItems);
        var length = Math.Clamp((bounds.Max - bounds.Min).Length() * 0.3f, 8, 30);
        var present = viewModel.SelectedItems.Count > 0;
        Axis(GizmoX, "X", Vector3.UnitX, Vector3.UnitY);
        Axis(GizmoY, "Y", Vector3.UnitY, Vector3.UnitX);
        Axis(GizmoZ, "Z", Vector3.UnitZ, Vector3.UnitX);
        void Axis(LineGeometryModel3D visual, string name, Vector3 axis, Vector3 side)
        {
            var lines = new LineBuilder();
            if (present)
            {
                var end = center + axis * length; gizmoHandles[name] = end;
                lines.AddLine(center, end);
                lines.AddLine(end, end - axis * 2 + side); lines.AddLine(end, end - axis * 2 - side);
            }
            visual.Geometry = lines.ToLineGeometry3D();
        }
    }

    private void OnImageDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var item = Pick(e.GetPosition(Viewport), out _);
        ToolSummary.Text = item is null ? "Suelta STL para importar · imagen: elige una pieza destino"
            : $"Destino de imagen: {item.Name} · sin emisión";
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void OnDropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || viewModel.IsBusy) return;
        var target = Pick(e.GetPosition(Viewport), out var hit) ?? viewModel.Selected;
        var stls = files.Where(p => string.Equals(System.IO.Path.GetExtension(p), ".stl", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (stls.Length > 0) await viewModel.ImportFilesAsync(stls, viewModel.UnitIndex switch { 1 => 10, 2 => 1000, 3 => 25.4f, _ => 1 });
        var images = files.Where(p => new[] { ".png", ".jpg", ".jpeg" }.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant()));
        foreach (var image in images)
        {
            if (target is null) { ToolSummary.Text = "Selecciona una pieza destino para añadir la imagen."; break; }
            Vector3? origin = null; Vector3? normal = null;
            if (hit is not null && hit.TriangleIndices?.Item1 is { } index)
            {
                var local = Transform(target.Model.Pose).Inverse?.Transform(new Point3D(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z));
                if (local is { } point) origin = new((float)point.X, (float)point.Y, (float)point.Z);
                if (index / 3 < target.Model.Mesh.TriangleCount) normal = target.Model.Mesh.FaceNormals[index / 3];
            }
            await viewModel.AddImageFileAsync(image, target.Model.Id, origin, normal);
        }
        e.Handled = true;
    }
}
