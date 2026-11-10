using System.Numerics;

namespace LaserMesh.Engraving;

/// <summary>Laser enable mode recorded by the planar G-code simulator.</summary>
public enum LaserMode
{
    /// <summary>No emission.</summary>
    Off,
    /// <summary>M3 constant power.</summary>
    Constant,
    /// <summary>M4 dynamic power.</summary>
    Dynamic
}

/// <summary>One simulated linear movement in millimetres.</summary>
/// <param name="Start">Starting position.</param>
/// <param name="End">Ending position.</param>
/// <param name="Rapid">Whether this is a non-engraving G0 move.</param>
/// <param name="Feed">Feed in millimetres per minute.</param>
/// <param name="Laser">Laser mode for this movement.</param>
/// <param name="Power">Unscaled S word from the input program.</param>
/// <param name="EndSeconds">Cumulative simulated time at the end of the segment.</param>
public readonly record struct ToolpathSegment(Vector3 Start, Vector3 End, bool Rapid, double Feed,
    LaserMode Laser, double Power, double EndSeconds);

/// <summary>Linear segments and their estimated playback time.</summary>
public sealed class Toolpath
{
    internal Toolpath(List<ToolpathSegment> segments) => Segments = segments.AsReadOnly();

    /// <summary>Segments in execution order.</summary>
    public IReadOnlyList<ToolpathSegment> Segments { get; }

    /// <summary>Estimated duration using programmed feed and a fixed rapid speed.</summary>
    public double DurationSeconds => Segments[^1].EndSeconds;

    /// <summary>Finds the interpolated tool position at an estimated playback time.</summary>
    public Vector3 PositionAt(double seconds)
    {
        var first = 0;
        var last = Segments.Count - 1;
        while (first < last)
        {
            var middle = first + (last - first) / 2;
            if (Segments[middle].EndSeconds < seconds)
                first = middle + 1;
            else
                last = middle;
        }

        var start = first == 0 ? 0 : Segments[first - 1].EndSeconds;
        var segment = Segments[first];
        var progress = Math.Clamp((seconds - start) / (segment.EndSeconds - start), 0, 1);
        return Vector3.Lerp(segment.Start, segment.End, (float)progress);
    }
}
