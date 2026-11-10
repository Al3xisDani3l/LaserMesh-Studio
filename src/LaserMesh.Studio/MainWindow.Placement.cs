using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using HelixToolkit.SharpDX;
using LaserMesh.Engraving;
using Microsoft.Win32;

namespace LaserMesh.Studio;

public partial class MainWindow
{
    private PlacementGuide? placementGuide;
    private CancellationTokenSource? placementWork;
    private bool placementEnabled;

    private void OnGeneratePlacement(object sender, RoutedEventArgs e)
    {
        placementEnabled = true;
        RefreshPlacement();
    }

    private void OnPlacementModeChanged(object sender, SelectionChangedEventArgs e) => RefreshPlacement();

    private async void RefreshPlacement()
    {
        if (!placementEnabled)
            return;

        placementWork?.Cancel();
        placementWork = null;
        placementGuide = null;
        PlacementOutline.Geometry = null;
        ExportPlacement.IsEnabled = false;

        if (viewModel.Selected is not { } selected)
        {
            PlacementSummary.Text = "Selecciona una malla para generar la guía.";
            return;
        }

        var mode = PlacementModePicker.SelectedIndex == 1 ? PlacementMode.Rectangle : PlacementMode.Silhouette;
        var model = selected.Model;
        var pose = model.Pose;
        var matrix = Matrix3x2.CreateScale((float)pose.Scale)
            * Matrix3x2.CreateRotation((float)(pose.Rotation * Math.PI / 180))
            * Matrix3x2.CreateTranslation((float)pose.X, (float)pose.Y);
        using var work = new CancellationTokenSource();
        placementWork = work;
        PlacementSummary.Text = $"Calculando guía · {model.Name}…";

        try
        {
            var (guide, geometry, z) = await Task.Run(() =>
            {
                var guide = PlacementGuide.Create(model.Mesh, matrix, mode, work.Token);
                var face = model.Mesh.HighestProjectableFace;
                var height = model.Mesh.Maximum.Z;
                if (face >= 0)
                {
                    var vertices = model.Mesh.Positions.Slice(face * 3, 3);
                    height = (vertices[0].Z + vertices[1].Z + vertices[2].Z) / 3;
                }
                var z = (float)(height * pose.Scale + pose.Z);
                var builder = new LineBuilder();
                foreach (var loop in guide.Loops)
                {
                    for (var i = 0; i < loop.Count; i++)
                    {
                        work.Token.ThrowIfCancellationRequested();
                        builder.AddLine(new(loop[i], z), new(loop[(i + 1) % loop.Count], z));
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
            PlacementSummary.Text = $"{model.Name} · {size.X:0.###} × {size.Y:0.###} mm · "
                + $"X {guide.Minimum.X:0.###} a {guide.Maximum.X:0.###} · Y {guide.Minimum.Y:0.###} a {guide.Maximum.Y:0.###} · Z {z:0.###}";
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        catch (InvalidDataException ex)
        {
            if (placementWork == work)
                PlacementSummary.Text = ex.Message;
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
