using LaserMesh.Calibration;

namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Observable simulation state, never a physical device command.</summary>
/// <param name="Mode">Chosen accessory mode.</param>
/// <param name="SurfaceMm">Completed surface travel.</param>
/// <param name="CommandMm">Corresponding calibrated motor travel.</param>
/// <param name="Angle">Object angle for cylinder mode.</param>
/// <param name="Finished">Whether the simulation reached its target.</param>
public sealed record AccessorySimulationState(AccessoryMode Mode, double SurfaceMm, double CommandMm, double Angle, bool Finished);

/// <summary>Time-based, cancellable preview of the three accessory configurations, with no transport.</summary>
public static class AccessorySimulator
{
    /// <summary>Runs simulated movement and reports a final stopped state on cancellation.</summary>
    public static async Task RunAsync(AccessoryCalibration calibration, double desiredMm,
        IProgress<AccessorySimulationState> progress, CancellationToken cancellationToken)
    {
        _ = calibration.CommandFor(desiredMm); double distance = 0;
        try
        {
            for (var step = 0; step <= 50; step++)
            {
                cancellationToken.ThrowIfCancellationRequested(); distance = desiredMm * step / 50;
                progress.Report(State(step == 50));
                if (step < 50) await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { progress.Report(State(false)); throw; }
        AccessorySimulationState State(bool finished) => new(calibration.Mode, distance, calibration.CommandFor(distance),
            calibration.Mode == AccessoryMode.Cylinder ? calibration.AngleFor(distance) : 0, finished);
    }
}

/// <summary>Read-only USB status probe; never sends settings, motion or emission commands.</summary>
public static class UsbDeviceProbe
{
    /// <summary>Queries state and firmware on an explicitly selected exclusive port.</summary>
    public static Task<(WorkingStatus Status, ushort Firmware)> ProbeAsync(string port, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            using var link = new UsbPreviewConnection(port);
            link.Send(LaserPeckerProtocol.Query(0));
            var status = LaserPeckerReplies.Status(link.Receive(f => f.Function == 0 && f.Payload.Length >= 21, cancellationToken));
            link.Send(LaserPeckerProtocol.Query(3));
            var firmware = LaserPeckerReplies.Version(link.Receive(f => f.Function == 0 && f.Payload.Length is >= 6 and < 21, cancellationToken)).Software;
            return (status, firmware);
        }, cancellationToken);
}
