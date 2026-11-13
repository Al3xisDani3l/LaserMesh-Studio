using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using HelixToolkit.SharpDX;
using LaserMesh.Engraving;
using Microsoft.Win32;
using LaserMesh.Projects;

namespace LaserMesh.Studio;

public partial class MainWindow
{
    private PlacementGuide? placementGuide;
    private CancellationTokenSource? placementWork;
    private bool placementEnabled = true;
    private long guideRevision;
    private bool restoringPreparation;

    private PreparationSettings CapturePreparation()
    {
        var mode = PlacementModePicker.SelectedIndex switch { 1 => "Rectangle", 2 => "Individual", 3 => "Section", _ => "Silhouette" };
        var reference = GuideReference.SelectedIndex switch { 1 => "Inferior", 2 => "Support", 3 => "Custom", 4 => "Face", _ => "Superior" };
        return new(mode, reference, GuideScope.SelectedIndex == 1, double.TryParse(GuideZ.Text, out var z) && double.IsFinite(z) ? z : 0)
        { FaceModel = faceReference?.ModelId, FacePoint = faceReference?.Point ?? Vector3.Zero, FaceNormal = faceReference?.Normal ?? Vector3.UnitZ };
    }

    private void RestorePreparation(PreparationSettings settings)
    {
        restoringPreparation = true;
        try
        {
            PlacementModePicker.SelectedIndex = settings.Mode switch { "Rectangle" => 1, "Individual" => 2, "Section" => 3, _ => 0 };
            GuideReference.SelectedIndex = settings.Reference switch { "Inferior" => 1, "Support" => 2, "Custom" => 3, "Face" => 4, _ => 0 };
            GuideScope.SelectedIndex = settings.AllVisible ? 1 : 0; GuideZ.Text = settings.Height.ToString(System.Globalization.CultureInfo.CurrentCulture);
            var owner = viewModel.Models.FirstOrDefault(m => m.Model.Id == settings.FaceModel);
            faceReference = owner is not null ? new FaceReference(owner.Model.Id, owner.Model.Mesh, settings.FacePoint, settings.FaceNormal) : null;
            UpdateSurfaceReference();
        }
        finally { restoringPreparation = false; }
        if (IsLoaded) RefreshPlacement();
    }

    private void OnGeneratePlacement(object sender, RoutedEventArgs e)
    {
        placementEnabled = true;
        RefreshPlacement();
    }

    private void OnPlacementModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !restoringPreparation) { viewModel.RecordPreparationChange(); RefreshPlacement(); }
    }
    private void OnGuideHeight(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && !restoringPreparation && double.TryParse(GuideZ.Text, out var z) && double.IsFinite(z))
        { viewModel.RecordPreparationChange(); RefreshPlacement(); }
    }

    private async void RefreshPlacement() => await RefreshPlacementAsync();

    private async Task RefreshPlacementAsync()
    {
        if (!placementEnabled)
            return;

        placementWork?.Cancel();
        placementWork = null;
        placementGuide = null;
        PlacementOutline.Geometry = null;
        ExportPlacement.IsEnabled = false;

        var scope = GuideScope?.SelectedIndex == 1 ? viewModel.Models.Where(m => m.Model.Visible).ToArray()
            : viewModel.SelectedItems.Where(m => m.Model.Visible).ToArray();
        if (scope.Length == 0)
        {
            PlacementSummary.Text = "Selecciona piezas o elige Toda la escena.";
            await StopLiveAsync();
            return;
        }
        var mode = PlacementModePicker.SelectedIndex switch { 1 => PlacementMode.Rectangle,
            2 => PlacementMode.Individual, 3 => PlacementMode.Section, _ => PlacementMode.Silhouette };
        var kind = GuideReference.SelectedIndex switch { 1 => ReferenceKind.Inferior, 2 => ReferenceKind.Support,
            3 => ReferenceKind.Custom, 4 => ReferenceKind.Face, _ => ReferenceKind.Superior };
        if (!double.TryParse(GuideZ.Text, out var planeZ) || !double.IsFinite(planeZ))
        { PlacementSummary.Text = "Introduce una altura Z válida."; return; }
        if (kind == ReferenceKind.Face && faceReference is null)
        { PlacementSummary.Text = "Elige una cara de referencia."; return; }
        var request = new GuideRequest(++guideRevision, scope.Select(m => new GuideModel(m.Model.Id, m.Model.Mesh,
            m.Model.Pose.Matrix * Matrix4x4.CreateTranslation(previewOffset.X, previewOffset.Y, 0))).ToArray(), mode,
            new SurfaceReference(kind, kind == ReferenceKind.Face ? referenceZ : kind == ReferenceKind.Support
                ? viewModel.CaptureDocument().SupportZ : planeZ)
            {
                Origin = new((float)referencePoint.X, (float)referencePoint.Y, (float)referencePoint.Z),
                Normal = faceReference is { } face && viewModel.Models.FirstOrDefault(m => m.Model.Id == face.ModelId) is { } owner
                    ? Vector3.Normalize(Vector3.TransformNormal(face.Normal, owner.Model.Pose.Matrix)) : Vector3.UnitZ
            }, planeZ);
        using var work = new CancellationTokenSource();
        placementWork = work;
        PlacementSummary.Text = $"Calculando guía · {scope.Length} pieza(s)…";

        try
        {
            var (guide, geometry, z) = await Task.Run(() =>
            {
                var result = PlacementGuide.Create(request, work.Token);
                var guide = result.Guide;
                var z = (float)result.Z;
                var builder = new LineBuilder();
                foreach (var loop in guide.Loops)
                {
                    for (var i = 0; i < loop.Count; i++)
                    {
                        work.Token.ThrowIfCancellationRequested();
                        builder.AddLine(result.Present(loop[i]), result.Present(loop[(i + 1) % loop.Count]));
                    }
                }
                return (guide, builder.ToLineGeometry3D(), z);
            }, work.Token);

            if (placementWork != work)
                return;
            placementGuide = guide;
            PlacementOutline.Geometry = geometry;
            ExportPlacement.IsEnabled = true;
            var size = guide.Maximum - guide.Minimum;
            PlacementSummary.Text = $"{scope.Length} pieza(s) · {size.X:0.###} × {size.Y:0.###} mm · "
                + $"X {guide.Minimum.X:0.###} a {guide.Maximum.X:0.###} · Y {guide.Minimum.Y:0.###} a {guide.Maximum.Y:0.###} · Z {z:0.###}";
            QueueLiveGuide(guide);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        catch (InvalidDataException ex)
        {
            if (placementWork == work)
            {
                PlacementSummary.Text = ex.Message;
                await StopLiveAsync();
            }
        }
        finally
        {
            if (placementWork == work)
                placementWork = null;
        }
    }

    private async void OnExportPlacement(object sender, RoutedEventArgs e)
    {
        if (placementGuide is not { } guide)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "Guía G-code (*.gcode)|*.gcode",
            DefaultExt = ".gcode",
            FileName = "Guia-colocacion.gcode"
        };
        if (dialog.ShowDialog(this) == true)
            await viewModel.ExportPlacementAsync(dialog.FileName, guide);
    }

    private void ReleasePlacement()
    {
        placementEnabled = false;
        placementWork?.Cancel();
        placementWork = null;
    }
}
