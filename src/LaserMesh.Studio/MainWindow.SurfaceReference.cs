using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.SharpDX;
using LaserMesh.Engraving;
using LaserMesh.Geometry;
using Vector3 = System.Numerics.Vector3;

namespace LaserMesh.Studio;

public partial class MainWindow
{
    private sealed record FaceReference(Guid ModelId, TriangleMesh Mesh, Vector3 Point, Vector3 Normal);

    private readonly MatrixTransform3D pathPlacement = new();
    private FaceReference? faceReference;
    private Point3D referencePoint;
    private double referenceZ;
    private bool choosingFace;
    private bool alignmentTest;

    private void OnSelectFace(object sender, RoutedEventArgs e)
    {
        SetFaceSelection(!choosingFace);
        if (choosingFace)
            FaceSummary.Text = "Haz clic en la cara de grabado.";
    }

    private void SetFaceSelection(bool enabled)
    {
        choosingFace = enabled;
        SelectFaceButton.Content = enabled ? "Cancelar selección" : "Elegir cara";
        Viewport.Cursor = enabled ? Cursors.Cross : null;
        if (!enabled)
            RefreshFaceSummary();
    }

    private void OnHighestFace(object sender, RoutedEventArgs e)
    {
        if (viewModel.Selected is not { } item)
        {
            FaceSummary.Text = "Selecciona una pieza para buscar su cara más alta.";
            return;
        }
        var mesh = item.Model.Mesh;
        var index = mesh.HighestProjectableFace;
        if (index < 0)
        {
            FaceSummary.Text = "No hay una cara apta para proyección vertical.";
            return;
        }

        var vertices = mesh.Positions.Slice(index * 3, 3);
        var center = (vertices[0] + vertices[1] + vertices[2]) / 3;
        SetSurfaceReference(item, center, mesh.FaceNormals[index]);
    }

    private void SelectHitFace(SceneItem item, HitTestResult hit)
    {
        var mesh = item.Model.Mesh;
        var index = hit.TriangleIndices?.Item1 / 3;
        if (index is not { } face || face < 0 || face >= mesh.TriangleCount)
        {
            FaceSummary.Text = "No se pudo identificar la cara. Elige otra.";
            return;
        }
        var world = hit.PointHit;
        var inverse = Transform(item.Model.Pose).Inverse!;
        var point = inverse.Transform(new Point3D(world.X, world.Y, world.Z));
        SetSurfaceReference(item, new((float)point.X, (float)point.Y, (float)point.Z), mesh.FaceNormals[face]);
    }

    private void SetSurfaceReference(SceneItem item, Vector3 point, Vector3 normal)
    {
        if (Math.Abs(normal.Z) < SurfaceProjection.MinimumVerticalComponent)
        {
            FaceSummary.Text = "La cara es casi vertical. Elige otra para proyectar desde arriba.";
            return;
        }

        faceReference = new(item.Model.Id, item.Model.Mesh, point, normal);
        SetFaceSelection(false);
        ClearFaceButton.IsEnabled = true;
        UpdateSurfaceReference(forcePlacement: true);
    }

    private void OnClearFace(object sender, RoutedEventArgs e) => ClearSurfaceReference();

    private void ClearSurfaceReference()
    {
        faceReference = null;
        referencePoint = new();
        referenceZ = 0;
        pathPlacement.Matrix = Matrix3D.Identity;
        ClearFaceButton.IsEnabled = false;
        SetFaceSelection(false);
        SyncField();
    }

    private void OnTrackPose(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && TrackPose.IsChecked == true)
            UpdateSurfaceReference(forcePlacement: true);
    }

    private void UpdateSurfaceReference(bool forcePlacement = false)
    {
        if (faceReference is not { } face)
            return;

        var item = viewModel.Models.FirstOrDefault(candidate => candidate.Model.Id == face.ModelId
            && ReferenceEquals(candidate.Model.Mesh, face.Mesh));
        if (item is null)
        {
            ClearSurfaceReference();
            return;
        }

        var pose = item.Model.Pose;
        var transform = Transform(pose);
        referencePoint = transform.Transform(new Point3D(face.Point.X, face.Point.Y, face.Point.Z));
        referenceZ = referencePoint.Z;
        if (forcePlacement || TrackPose.IsChecked == true)
        {
            var direction = transform.Transform(new Vector3D(face.Normal.X, face.Normal.Y, face.Normal.Z));
            direction.Normalize();
            var matrix = SurfaceProjection.Create(new((float)referencePoint.X, (float)referencePoint.Y, (float)referencePoint.Z),
                new((float)direction.X, (float)direction.Y, (float)direction.Z), pose.Rotation);
            pathPlacement.Matrix = new Matrix3D(
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44);
        }
        SyncField();
    }

    private void RefreshFaceSummary()
    {
        if (choosingFace)
            return;
        if (faceReference is null)
        {
            FaceSummary.Text = "Origen XY (0.0, 0.0) · Z 0";
            return;
        }

        var field = viewModel.Field;
        var distance = field.Height - referenceZ;
        var section = referenceZ >= 0 && distance > 0
            ? $"sección {field.WidthAt(referenceZ):0.#} mm · lente a {distance:0.#} mm"
            : "referencia fuera del campo";
        FaceSummary.Text = $"Cara Z {referenceZ:0.##} mm · {section} · origen {referencePoint.X:0.##}, {referencePoint.Y:0.##}";
    }

    private void OnAlignmentTest(object sender, RoutedEventArgs e)
    {
        if (viewModel.Toolpath is null)
            return;
        PausePlayback();
        var start = !alignmentTest;
        SetAlignmentGuide(start);
        if (start)
        {
            PlaybackSlider.Value = 0;
            OnTogglePlayback(sender, e);
        }
    }

    private void SetAlignmentGuide(bool enabled)
    {
        alignmentTest = enabled;
        AlignmentButton.Content = enabled ? "Terminar prueba virtual" : "Prueba de posición";
        AlignmentGuide.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
        {
            AlignmentGuide.Geometry = null;
            return;
        }

        var lines = new LineBuilder();
        var size = Math.Max(toolpathBounds.SizeX, toolpathBounds.SizeY);
        var cross = (float)Math.Clamp(size * 0.035, 0.5, 15);
        lines.AddLine(new(-cross, 0, 0), new(cross, 0, 0));
        lines.AddLine(new(0, -cross, 0), new(0, cross, 0));

        if (!toolpathBounds.IsEmpty)
        {
            var minX = (float)toolpathBounds.X;
            var maxX = (float)(toolpathBounds.X + toolpathBounds.SizeX);
            var minY = (float)toolpathBounds.Y;
            var maxY = (float)(toolpathBounds.Y + toolpathBounds.SizeY);
            lines.AddLine(new(minX, minY, 0), new(maxX, minY, 0));
            lines.AddLine(new(maxX, minY, 0), new(maxX, maxY, 0));
            lines.AddLine(new(maxX, maxY, 0), new(minX, maxY, 0));
            lines.AddLine(new(minX, maxY, 0), new(minX, minY, 0));
        }
        AlignmentGuide.Geometry = lines.ToLineGeometry3D();
    }
}
