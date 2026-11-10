using System.Numerics;
using HelixToolkit;
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
}
