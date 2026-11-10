using System.Numerics;
using System.Globalization;
using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using LaserMesh.Geometry;

namespace LaserMesh.Rendering;

/// <summary>Builds Helix geometry from the renderer-independent mesh.</summary>
public sealed class MeshAdapter
{
    /// <summary>Prepares a mesh and its picking index off the UI thread.</summary>
    public Task<MeshGeometry3D> PrepareAsync(TriangleMesh mesh, CancellationToken cancellationToken)
        => Task.Run(() => Prepare(mesh, cancellationToken), cancellationToken);

    private static MeshGeometry3D Prepare(TriangleMesh mesh, CancellationToken token)
    {
        var positions = new Vector3Collection(mesh.Positions.Length);
        var normals = new Vector3Collection(mesh.Positions.Length);
        var indices = new IntCollection(mesh.Positions.Length);
        var source = mesh.Positions;
        var faces = mesh.FaceNormals;

        for (var i = 0; i < faces.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            for (var j = 0; j < 3; j++)
            {
                var index = i * 3 + j;
                positions.Add(source[index]);
                normals.Add(faces[i]);
                indices.Add(index);
            }
        }

        var geometry = new MeshGeometry3D { Positions = positions, Normals = normals, Indices = indices };
        geometry.UpdateOctree();
        token.ThrowIfCancellationRequested();
        return geometry;
    }

    /// <summary>Creates a line grid on the XY plane with a specified spacing.</summary>
    public LineGeometry3D CreateGrid(float spacing)
    {
        var builder = new LineBuilder();
        var halfWidth = spacing * 10;
        for (var i = -10; i <= 10; i++)
        {
            var at = i * spacing;
            builder.AddLine(new(-halfWidth, at, 0), new(halfWidth, at, 0));
            builder.AddLine(new(at, -halfWidth, 0), new(at, halfWidth, 0));
        }
        return builder.ToLineGeometry3D();
    }

    /// <summary>Creates one axis line starting at the world origin.</summary>
    public LineGeometry3D CreateAxis(Vector3 end)
    {
        var builder = new LineBuilder();
        builder.AddLine(Vector3.Zero, end);
        return builder.ToLineGeometry3D();
    }

    /// <summary>Labels signed X/Y distances on the scene grid, with program zero at its center.</summary>
    public BillboardText3D CreateGridLabels(float spacing)
    {
        var labels = new BillboardText3D();
        Add("0.0", new(spacing * 0.5f, -spacing * 0.6f, 0.001f), new(0.95f, 0.92f, 0.74f, 1));
        for (var i = -8; i <= 8; i += 4)
        {
            if (i == 0)
                continue;
            var distance = i * spacing;
            var text = distance.ToString("G4", CultureInfo.InvariantCulture);
            Add($"X {text} mm", new(distance, -spacing * 0.6f, 0.001f), new(0.88f, 0.66f, 0.69f, 1));
            Add($"Y {text} mm", new(-spacing * 0.7f, distance, 0.001f), new(0.60f, 0.81f, 0.72f, 1));
        }
        return labels;

        void Add(string text, Vector3 position, Color4 color)
        {
            labels.TextInfo.Add(new HelixToolkit.SharpDX.TextInfo(text, position) { Foreground = color, Scale = 0.72f });
        }
    }
}
