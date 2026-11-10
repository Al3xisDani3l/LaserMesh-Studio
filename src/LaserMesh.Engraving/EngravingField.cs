using System.Numerics;

namespace LaserMesh.Engraving;

/// <summary>Square-pyramid estimate of the LP2 scan envelope, in scene millimetres.</summary>
public readonly record struct EngravingField
{
    /// <summary>Nominal LP2 lens-to-work distance.</summary>
    public const double NominalHeight = 110;

    /// <summary>Nominal LP2 planar working width.</summary>
    public const double NominalWidth = 100;

    /// <summary>Creates an estimate with the lens on the Z axis and work plane at Z = 0.</summary>
    public EngravingField(double height)
    {
        if (!double.IsFinite(height) || height is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(height));
        Height = height;
    }

    /// <summary>Lens height above the scene work plane.</summary>
    public double Height { get; }

    /// <summary>Geometric width at the work plane, with no optical performance guarantee.</summary>
    public double PlaneWidth => NominalWidth * Height / NominalHeight;

    /// <summary>Tests the geometric envelope only, independent of focus or device limits.</summary>
    public bool Contains(Vector3 point)
    {
        if (point.Z < 0 || point.Z > Height)
            return false;
        var halfWidth = NominalWidth * (Height - point.Z) / (2 * NominalHeight);
        return Math.Abs(point.X) <= halfWidth && Math.Abs(point.Y) <= halfWidth;
    }
}
