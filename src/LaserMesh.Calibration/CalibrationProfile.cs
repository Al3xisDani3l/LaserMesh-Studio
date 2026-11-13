using System.Numerics;
using System.Text.Json;

namespace LaserMesh.Calibration;

/// <summary>Attachment configuration, not a working-state byte.</summary>
public enum AccessoryMode
{
    /// <summary>Fixed head without accessory motion.</summary>
    Fixed,
    /// <summary>Object rotation on rollers.</summary>
    Cylinder,
    /// <summary>Linear material feed.</summary>
    Slab,
    /// <summary>Head travelling on the accessory.</summary>
    Trolley
}

/// <summary>Measured affine scene-to-laser registration and configuration fingerprint.</summary>
/// <param name="Id">Profile identity.</param>
/// <param name="Device">Hardware identity.</param>
/// <param name="Firmware">Verified firmware identifier.</param>
/// <param name="Mode">Accessory configuration.</param>
/// <param name="Configuration">Fixture, focus and geometry fingerprint.</param>
/// <param name="Transform">Scene-to-laser affine registration.</param>
/// <param name="RmsError">Measured RMS registration error in millimetres.</param>
/// <param name="MeasuredAt">Time of physical measurements.</param>
public sealed record CalibrationProfile(Guid Id, string Device, string Firmware, AccessoryMode Mode,
    string Configuration, Matrix4x4 Transform, double RmsError, DateTimeOffset MeasuredAt)
{
    /// <summary>Indicates configuration agreement; does not assert optical focus or emission validation.</summary>
    public bool Matches(string device, string firmware, AccessoryMode mode, string configuration)
        => Device == device && Firmware == firmware && Mode == mode && Configuration == configuration
            && double.IsFinite(RmsError) && RmsError >= 0;
}

/// <summary>Atomic storage of reusable physical calibration profiles.</summary>
public interface ICalibrationStore
{
    /// <summary>Saves one measured profile.</summary>
    Task SaveAsync(CalibrationProfile profile, CancellationToken cancellationToken);
    /// <summary>Loads a profile by its stable identity.</summary>
    Task<CalibrationProfile> LoadAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Local JSON calibration store with atomic replacement.</summary>
public sealed class CalibrationStore(string directory) : ICalibrationStore
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    /// <inheritdoc/>
    public async Task SaveAsync(CalibrationProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Device) || !double.IsFinite(profile.RmsError)
            || profile.RmsError < 0 || !Enum.IsDefined(profile.Mode) || !Matrix4x4.Invert(profile.Transform, out _))
            throw new InvalidDataException("Perfil de calibración inválido.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{profile.Id:N}.json"); var temp = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, profile), Options), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }
    /// <inheritdoc/>
    public async Task<CalibrationProfile> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, $"{id:N}.json"), cancellationToken);
        if (bytes.Length > 65536) throw new InvalidDataException("Perfil demasiado grande.");
        var envelope = JsonSerializer.Deserialize<Envelope>(bytes, Options);
        if (envelope is not { Version: 1 } || envelope.Profile.Id != id || !Matrix4x4.Invert(envelope.Profile.Transform, out _)
            || !double.IsFinite(envelope.Profile.RmsError) || envelope.Profile.RmsError < 0)
            throw new InvalidDataException("Perfil o versión no válidos.");
        return envelope.Profile;
    }
    private sealed record Envelope(int Version, CalibrationProfile Profile);
}

/// <summary>Least-squares XY calibration from measured point correspondences.</summary>
public static class AffineCalibration
{
    /// <summary>Fits an affine transform; rejects degenerate or non-finite measurements.</summary>
    public static (Matrix4x4 Transform, double Rms) Fit(IReadOnlyList<Vector2> scene, IReadOnlyList<Vector2> measured)
    {
        if (scene.Count != measured.Count || scene.Count < 3
            || scene.Concat(measured).Any(p => !float.IsFinite(p.LengthSquared())))
            throw new ArgumentException("Se necesitan tres o más correspondencias finitas.");
        var system = new double[3, 5];
        for (var i = 0; i < scene.Count; i++)
        {
            double[] row = [scene[i].X, scene[i].Y, 1];
            for (var r = 0; r < 3; r++)
            {
                for (var c = 0; c < 3; c++) system[r, c] += row[r] * row[c];
                system[r, 3] += row[r] * measured[i].X; system[r, 4] += row[r] * measured[i].Y;
            }
        }
        for (var pivot = 0; pivot < 3; pivot++)
        {
            var best = Enumerable.Range(pivot, 3 - pivot).MaxBy(r => Math.Abs(system[r, pivot]));
            if (Math.Abs(system[best, pivot]) < 1e-10) throw new ArgumentException("Puntos de calibración colineales.");
            for (var c = 0; c < 5; c++) (system[pivot, c], system[best, c]) = (system[best, c], system[pivot, c]);
            var d = system[pivot, pivot]; for (var c = 0; c < 5; c++) system[pivot, c] /= d;
            for (var r = 0; r < 3; r++) if (r != pivot)
            { var f = system[r, pivot]; for (var c = 0; c < 5; c++) system[r, c] -= f * system[pivot, c]; }
        }
        var matrix = Matrix4x4.Identity;
        matrix.M11 = (float)system[0, 3]; matrix.M21 = (float)system[1, 3]; matrix.M41 = (float)system[2, 3];
        matrix.M12 = (float)system[0, 4]; matrix.M22 = (float)system[1, 4]; matrix.M42 = (float)system[2, 4];
        if (!Matrix4x4.Invert(matrix, out _)) throw new ArgumentException("Registro no invertible.");
        var error = Enumerable.Range(0, scene.Count).Sum(i => Vector2.DistanceSquared(Vector2.Transform(scene[i], matrix), measured[i]));
        return (matrix, Math.Sqrt(error / scene.Count));
    }
}
