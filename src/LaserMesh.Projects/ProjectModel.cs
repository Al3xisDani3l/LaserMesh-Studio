using LaserMesh.Geometry;
using System.Numerics;
using System.Text.Json.Serialization;

namespace LaserMesh.Projects;

/// <summary>Mesh instance embedded in a project.</summary>
/// <param name="Id">Stable instance identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Mesh">Geometry in millimetres.</param>
/// <param name="Pose">Object placement.</param>
public sealed record ProjectModel(Guid Id, string Name, TriangleMesh Mesh, ModelPose Pose)
{
    /// <summary>Whether this instance is visible and participates in scene-wide preparation.</summary>
    public bool Visible { get; init; } = true;
    /// <summary>Prevents interactive modification.</summary>
    public bool Locked { get; init; }
    /// <summary>Optional editing group identity.</summary>
    public Guid? GroupId { get; init; }
}

/// <summary>Translation in millimetres, Z rotation in degrees and uniform scale.</summary>
/// <param name="X">X translation.</param>
/// <param name="Y">Y translation.</param>
/// <param name="Z">Z translation.</param>
/// <param name="Rotation">Angle about +Z.</param>
/// <param name="Scale">Uniform scale factor.</param>
public readonly record struct ModelPose(double X, double Y, double Z, double Rotation, double Scale)
{
    /// <summary>Placement without any transform.</summary>
    public static ModelPose Identity => new(0, 0, 0, 0, 1);

    /// <summary>Optional full orientation; absent in version-one projects.</summary>
    public Quaternion? Orientation { get; init; }

    /// <summary>Effective rotation, including legacy Z angles.</summary>
    [JsonIgnore]
    public Quaternion EffectiveOrientation => Quaternion.Normalize(
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(Rotation * Math.PI / 180))
        * (Orientation ?? Quaternion.Identity));

    /// <summary>Object-to-scene transform using row-vector System.Numerics conventions.</summary>
    [JsonIgnore]
    public Matrix4x4 Matrix => Matrix4x4.CreateScale((float)Scale)
        * Matrix4x4.CreateFromQuaternion(EffectiveOrientation)
        * Matrix4x4.CreateTranslation((float)X, (float)Y, (float)Z);

    /// <summary>XYZ Euler angles in degrees, decomposed as row-vector Rx * Ry * Rz.</summary>
    [JsonIgnore]
    public Vector3 EulerDegrees
    {
        get
        {
            var m = Matrix4x4.CreateFromQuaternion(EffectiveOrientation);
            var y = Math.Asin(Math.Clamp(-m.M13, -1, 1));
            var x = Math.Abs(Math.Cos(y)) > 0.000001 ? Math.Atan2(m.M23, m.M33) : Math.Atan2(-m.M32, m.M22);
            var z = Math.Abs(Math.Cos(y)) > 0.000001 ? Math.Atan2(m.M12, m.M11) : 0;
            return new((float)(x * 180 / Math.PI), (float)(y * 180 / Math.PI), (float)(z * 180 / Math.PI));
        }
    }

    /// <summary>Applies a scene-space uniform affine delta.</summary>
    public ModelPose Transform(Matrix4x4 delta)
    {
        if (!Matrix4x4.Decompose(Matrix * delta, out var scale, out var orientation, out var position)
            || Math.Abs(scale.X - scale.Y) > 0.001 || Math.Abs(scale.Y - scale.Z) > 0.001)
            throw new ArgumentException("La transformación requiere escala uniforme.", nameof(delta));
        var result = new ModelPose(position.X, position.Y, position.Z, 0, scale.X) { Orientation = orientation };
        if (!result.IsValid()) throw new ArgumentOutOfRangeException(nameof(delta));
        return result;
    }

    /// <summary>Indicates whether the pose is within supported numeric bounds.</summary>
    public bool IsValid() => Math.Abs(X) <= 1e9 && Math.Abs(Y) <= 1e9 && Math.Abs(Z) <= 1e9
        && Math.Abs(Rotation) <= 360 && Scale is >= 0.0001 and <= 1000
        && (Orientation is not { } q || float.IsFinite(q.LengthSquared()) && Math.Abs(q.LengthSquared() - 1) < 0.001);
}
