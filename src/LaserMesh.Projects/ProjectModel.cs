using LaserMesh.Geometry;

namespace LaserMesh.Projects;

/// <summary>Mesh instance embedded in a project.</summary>
/// <param name="Id">Stable instance identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Mesh">Geometry in millimetres.</param>
/// <param name="Pose">Object placement.</param>
public sealed record ProjectModel(Guid Id, string Name, TriangleMesh Mesh, ModelPose Pose);

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

    /// <summary>Indicates whether the pose is within supported numeric bounds.</summary>
    public bool IsValid() => Math.Abs(X) <= 1e9 && Math.Abs(Y) <= 1e9 && Math.Abs(Z) <= 1e9
        && Math.Abs(Rotation) <= 360 && Scale is >= 0.0001 and <= 1000;
}
