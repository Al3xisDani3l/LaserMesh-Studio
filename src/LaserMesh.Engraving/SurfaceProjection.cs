using System.Numerics;

namespace LaserMesh.Engraving;

/// <summary>Projects program millimetres from a local XY plane onto a face in scene coordinates.</summary>
public static class SurfaceProjection
{
    /// <summary>Minimum absolute Z component for a unit face normal in top-down projection.</summary>
    public const float MinimumVerticalComponent = 0.1f;

    /// <summary>Creates an affine placement with a rotated XY footprint and Z relative to the face plane.</summary>
    /// <param name="origin">Scene coordinates of program zero.</param>
    /// <param name="normal">Unit face normal in scene coordinates.</param>
    /// <param name="rotationDegrees">Program XY rotation about the scene Z axis.</param>
    /// <returns>Matrix from program coordinates to scene millimetres.</returns>
    public static Matrix4x4 Create(Vector3 origin, Vector3 normal, double rotationDegrees)
    {
        if (!(Math.Abs(normal.Z) >= MinimumVerticalComponent))
            throw new ArgumentException("La cara es casi vertical para una proyección desde arriba.", nameof(normal));

        var slope = Matrix4x4.Identity;
        slope.M13 = -normal.X / normal.Z;
        slope.M23 = -normal.Y / normal.Z;
        return Matrix4x4.CreateRotationZ((float)(rotationDegrees * Math.PI / 180))
            * slope * Matrix4x4.CreateTranslation(origin);
    }
}
