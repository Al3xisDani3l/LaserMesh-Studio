// SPDX-License-Identifier: GPL-3.0-or-later
// Raster wire encoding adapted from PyroGraph protocol.py. See NOTICE.md.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using LaserMesh.Calibration;
using LaserMesh.Imaging;

namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Validated operator parameters; preview levels are never used as engraving defaults.</summary>
/// <param name="Power">Explicit engraving power, 1 through 100.</param>
/// <param name="Depth">Explicit device depth, 1 through 100.</param>
/// <param name="Repeats">Number of passes, 1 through 255.</param>
public sealed record EngravingParameters(int Power, int Depth, int Repeats);

/// <summary>Physical validation record bound to firmware, attachment and calibrated configuration.</summary>
/// <param name="Firmware">Verified firmware.</param>
/// <param name="Mode">Verified attachment mode.</param>
/// <param name="Configuration">Configuration fingerprint.</param>
/// <param name="Evidence">External supervised validation record; empty means not validated.</param>
public sealed record EngravingProfile(string Firmware, AccessoryMode Mode, string Configuration, string Evidence)
{
    /// <summary>Measured focus plane in calibrated scene millimetres; absent until physically characterized.</summary>
    public double? FocusZ { get; init; }
    /// <summary>Measured permitted distance from focus, not an estimated compensation rule.</summary>
    public double? FocusTolerance { get; init; }
    /// <summary>Whether this profile includes an explicit supervised physical acceptance record.</summary>
    public bool PhysicallyValidated => !string.IsNullOrWhiteSpace(Evidence)
        && FocusZ is { } focus && double.IsFinite(focus) && FocusTolerance is { } tolerance && double.IsFinite(tolerance) && tolerance >= 0;
}

/// <summary>Frozen file, start command and content hash for one prepared raster job.</summary>
public sealed class EngravingJob
{
    private readonly byte[] file;
    private readonly byte[] start;
    private EngravingJob(byte[] file, byte[] start, EngravingProfile profile, float minimumZ, float maximumZ)
    { this.file = file; this.start = start; Profile = profile; MinimumSurfaceZ = minimumZ; MaximumSurfaceZ = maximumZ;
        Fingerprint = Convert.ToHexString(SHA256.HashData(file.Concat(start).ToArray())); }
    /// <summary>Read-only encoded file including header.</summary>
    public ReadOnlyMemory<byte> File => file;
    /// <summary>Read-only engraving-start frame.</summary>
    public ReadOnlyMemory<byte> Start => start;
    /// <summary>Immutable execution profile.</summary>
    public EngravingProfile Profile { get; }
    /// <summary>Hash binding content and operator parameters.</summary>
    public string Fingerprint { get; }
    /// <summary>Lowest visible surface of the prepared image.</summary>
    public float MinimumSurfaceZ { get; }
    /// <summary>Highest visible surface of the prepared image.</summary>
    public float MaximumSurfaceZ { get; }

    /// <summary>Prepares LP2 raster format with explicit device-space offsets, no transmission.</summary>
    public static EngravingJob Create(RasterImage raster, double xMm, double yMm, int precision,
        EngravingParameters parameters, EngravingProfile profile, string name = "LaserMesh", float minimumZ = 0, float maximumZ = 0)
    {
        if (parameters.Power is < 1 or > 100 || parameters.Depth is < 1 or > 100 || parameters.Repeats is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(parameters));
        if (!float.IsFinite(minimumZ) || !float.IsFinite(maximumZ) || maximumZ < minimumZ)
            throw new ArgumentOutOfRangeException(nameof(minimumZ));
        var dpi = precision switch { 4 => 254d, 3 => 338.66666, 2 => 508, _ => throw new ArgumentOutOfRangeException(nameof(precision)) };
        var expected = raster.Packed ? ((raster.Width + 7) / 8) * (long)raster.Height : (long)raster.Width * raster.Height;
        if (raster.Width is <= 0 or > 65535 || raster.Height is <= 0 or > 65535 || expected != raster.Payload.Length
            || expected > 16_777_216 || !double.IsFinite(xMm) || !double.IsFinite(yMm) || xMm < 0 || yMm < 0
            || xMm + raster.Width * 25.4 / dpi > 100.001 || yMm + raster.Height * 25.4 / dpi > 100.001)
            throw new ArgumentOutOfRangeException(nameof(raster), "Raster fuera del campo fijo validado de 100 × 100 mm.");
        if (profile.Mode != AccessoryMode.Fixed)
            throw new NotSupportedException("La secuencia de grabado del accesorio aún necesita verificación de protocolo.");
        var nx = (int)(xMm * dpi / 25.4); var ny = (int)(yMm * dpi / 25.4);
        var file = new byte[64 + raster.Payload.Length]; file[0] = raster.Packed ? (byte)0x60 : (byte)0x10;
        Put16(1, raster.Width); Put16(3, raster.Height); file[9] = (byte)precision;
        Put16(10, nx); Put16(12, ny); Put16(14, (int)dpi);
        var encodedName = Encoding.UTF8.GetBytes(name); encodedName.AsSpan(0, Math.Min(encodedName.Length, 29)).CopyTo(file.AsSpan(34));
        raster.Payload.CopyTo(file, 64);
        var id = BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(file)); if (id == 0) id = 1;
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(5), id);
        var start = LaserPeckerProtocol.EncodeFields(1, (1, 1), (parameters.Power, 1), (101 - parameters.Depth, 1),
            (id, 4), (nx, 2), (ny, 2), (0, 1), (parameters.Repeats, 1), (0, 1), (0, 2), (0, 1), (0, 1), (60, 1), (0, 2), (1, 1));
        return new(file, start, profile, minimumZ, maximumZ);
        void Put16(int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(offset), checked((ushort)value));
    }
}

/// <summary>Owns one raster transfer and execution, keeping physical validation distinct from simulation.</summary>
public sealed class RasterEngravingSession
{
    /// <summary>True when cancellation/error shutdown could not be confirmed.</summary>
    public bool UnknownState { get; private set; }

    /// <summary>Executes with an exclusive transport; real mode requires measured calibration and physical acceptance.</summary>
    public Task RunAsync(Func<IPreviewConnection> connect, EngravingJob job, CalibrationProfile? calibration,
        bool simulation, IProgress<string> progress, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            if (UnknownState) throw new InvalidOperationException("Estado del láser desconocido.");
            if (!simulation && (!job.Profile.PhysicallyValidated || calibration is null
                || calibration.Firmware != job.Profile.Firmware || calibration.Mode != job.Profile.Mode
                || calibration.Configuration != job.Profile.Configuration
                || Math.Abs(job.MinimumSurfaceZ - job.Profile.FocusZ!.Value) > job.Profile.FocusTolerance!.Value
                || Math.Abs(job.MaximumSurfaceZ - job.Profile.FocusZ!.Value) > job.Profile.FocusTolerance!.Value))
                throw new InvalidOperationException("Grabado físico pendiente de validación supervisada y calibración compatible.");
            using var link = connect(); var touched = false;
            try
            {
                var initial = Status(cancellationToken);
                if (!initial.Finished || initial.Error != 0) throw new IOException("El LP2 no está en reposo.");
                link.Send(LaserPeckerProtocol.Query(3));
                var firmware = LaserPeckerReplies.Version(link.Receive(f => f.Function == 0 && f.Payload.Length is >= 6 and < 21, cancellationToken));
                if (firmware.Software.ToString(System.Globalization.CultureInfo.InvariantCulture) != job.Profile.Firmware || initial.ActiveLink != 2)
                    throw new IOException("Firmware o enlace USB incompatibles con el perfil preparado.");
                if (!simulation)
                {
                    link.Send(LaserPeckerProtocol.Query(8));
                    var name = LaserPeckerReplies.Name(link.Receive(f => f.Function == 0 && f.Payload.Length >= 3, cancellationToken));
                    if (!string.Equals(name, calibration!.Device, StringComparison.Ordinal))
                        throw new IOException("La identidad USB no coincide con el dispositivo calibrado.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                touched = true;
                link.Send(LaserPeckerProtocol.EncodeFields(5, (1, 1), (job.File.Length, 4), (0, 1)));
                Ack(1); link.Send(job.File[..64].ToArray()); Pause(100, cancellationToken);
                link.Send(job.File[64..].ToArray(), cancellationToken); Ack(2);
                cancellationToken.ThrowIfCancellationRequested();
                link.Send(job.Start.ToArray()); progress.Report(simulation ? "SIMULACIÓN · grabando raster" : "GRABANDO · raster LP2");
                var activeSeen = false;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromHours(2));
                while (true)
                {
                    Pause(250, deadline.Token); var state = Status(deadline.Token);
                    if (state.Error != 0) throw new IOException($"LP2 error {state.Error}.");
                    if (state.Mode == 1) activeSeen = true;
                    if (state.Finished && activeSeen) { touched = false; progress.Report("Trabajo terminado · reposo confirmado"); break; }
                    if (!activeSeen && state.Finished) throw new IOException("El dispositivo no confirmó inicio de grabado.");
                }
            }
            finally
            {
                if (touched)
                {
                    try
                    {
                        link.Send(LaserPeckerProtocol.Encode(0xFF, [0, 0, 0, 0, 0]));
                        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8)); Pause(1200, stop.Token);
                        if (!Status(stop.Token).Finished) throw new IOException("Sin reposo.");
                        progress.Report("Grabado detenido · reposo confirmado");
                    }
                    catch (Exception ex) { UnknownState = true; throw new IOException("Estado del láser desconocido. Usa parada física.", ex); }
                }
            }
            WorkingStatus Status(CancellationToken token)
            { link.Send(LaserPeckerProtocol.Query(0)); return LaserPeckerReplies.Status(link.Receive(f => f.Function == 0 && f.Payload.Length >= 21, token)); }
            void Ack(byte state)
            {
                var f = link.Receive(f => f.Function == 5 && f.Payload.Length >= 2 && f.Payload.Span[0] == state
                    && (f.Payload.Length < 3 || f.Payload.Span[2] == 0), cancellationToken);
                if (LaserPeckerReplies.FileAcknowledgement(f) != 1) throw new IOException("Carga raster rechazada.");
            }
        }, CancellationToken.None);
    private static void Pause(int ms, CancellationToken token) { token.WaitHandle.WaitOne(ms); token.ThrowIfCancellationRequested(); }
}

/// <summary>Measured accessory travel used for simulation and calibration diagnostics.</summary>
/// <param name="Mode">Cylinder, slab or trolley.</param>
/// <param name="CommandedMm">Commanded test travel.</param>
/// <param name="MeasuredMm">Measured travel along the material surface.</param>
/// <param name="DiameterMm">Cylinder diameter; zero for linear modes.</param>
/// <param name="Direction">Observed sign, minus or plus one.</param>
public sealed record AccessoryCalibration(AccessoryMode Mode, double CommandedMm, double MeasuredMm, double DiameterMm, int Direction)
{
    /// <summary>Converts desired surface travel into corrected commanded distance; emits no motion.</summary>
    public double CommandFor(double desiredMm)
    {
        if (Mode == AccessoryMode.Fixed || !Enum.IsDefined(Mode) || !double.IsFinite(CommandedMm) || CommandedMm <= 0
            || !double.IsFinite(MeasuredMm) || MeasuredMm <= 0 || Direction is not (-1 or 1)
            || !double.IsFinite(desiredMm) || Mode == AccessoryMode.Cylinder && (!double.IsFinite(DiameterMm) || DiameterMm <= 0))
            throw new InvalidDataException("Calibración de accesorio incompleta.");
        return desiredMm * CommandedMm / MeasuredMm * Direction;
    }
    /// <summary>Converts surface travel to simulated object angle, only in cylinder mode.</summary>
    public double AngleFor(double desiredMm)
        => Mode == AccessoryMode.Cylinder ? CommandFor(desiredMm) / (Math.PI * DiameterMm) * 360
            : throw new InvalidOperationException("Este accesorio está en avance lineal.");
}
