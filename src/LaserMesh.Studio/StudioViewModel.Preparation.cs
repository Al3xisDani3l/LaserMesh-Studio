using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserMesh.Calibration;
using LaserMesh.Hardware.LaserPecker;
using LaserMesh.Imaging;
using Microsoft.Win32;

namespace LaserMesh.Studio;

internal sealed partial class StudioViewModel
{
    [ObservableProperty] public partial string EngravingPower { get; set; } = "";
    [ObservableProperty] public partial string EngravingDepth { get; set; } = "";
    [ObservableProperty] public partial string EngravingRepeats { get; set; } = "1";
    [ObservableProperty] public partial int AccessoryIndex { get; set; }
    [ObservableProperty] public partial string AccessoryDiameter { get; set; } = "40";
    [ObservableProperty] public partial string CommandedTravel { get; set; } = "100";
    [ObservableProperty] public partial string MeasuredTravel { get; set; } = "";
    [ObservableProperty] public partial string DesiredTravel { get; set; } = "10";
    [ObservableProperty] public partial string TravelDirection { get; set; } = "1";
    [ObservableProperty] public partial string AccessorySummary { get; private set; } = "Accesorio desconectado · modos físicos sin verificar";
    [ObservableProperty] public partial string PreparationStatus { get; private set; } = "Simulación · grabado físico pendiente de aceptación supervisada";
    private CalibrationProfile? calibration;
    private EngravingProfile? validatedEngravingProfile;
    private readonly RasterEngravingSession rasterSession = new();
    private Task? rasterExecution;
    private CancellationTokenSource? rasterCancellation;
    internal event Action<bool>? PhysicalEngravingChanged;
    public bool CanEngraveReal => !rasterSession.UnknownState && validatedEngravingProfile is { PhysicallyValidated: true, Mode: AccessoryMode.Fixed }
        && calibration is not null && validatedEngravingProfile.Configuration == SetupFingerprint
        && calibration.Matches(BluetoothName, validatedEngravingProfile.Firmware, validatedEngravingProfile.Mode, SetupFingerprint);
    [ObservableProperty] public partial bool UseMeasuredCalibration { get; set; }
    [ObservableProperty] public partial bool DevelopCylinder { get; set; }
    [ObservableProperty] public partial string SetupFingerprint { get; set; } = "";
    partial void OnUseMeasuredCalibrationChanged(bool value) => InvalidatePreparedImage();
    partial void OnDevelopCylinderChanged(bool value) => InvalidatePreparedImage();
    partial void OnSetupFingerprintChanged(string value) { InvalidatePreparedImage(); OnPropertyChanged(nameof(CanEngraveReal)); }
    [ObservableProperty] public partial string UsbPortName { get; set; } = "COM8";
    [ObservableProperty] public partial string DeviceStatus { get; private set; } = "Simulación · sin sesión física";
    internal void ReportDeviceStatus(string message) => DeviceStatus = message;
    [RelayCommand]
    private async Task ProbeUsbAsync()
    {
        if (IsBusy) return;
        await RunAsync("Consultando USB sin emisión", async token =>
        {
            var (status, firmware) = await UsbDeviceProbe.ProbeAsync(UsbPortName.Trim(), token);
            DeviceStatus = $"USB · FW {firmware} · modo {status.Mode} · error {status.Error}";
            AccessorySummary = $"Reportado por LP2: rotación {status.RotaryConnection}, avance {status.SlideConnection}, carrito {status.CarConnection}. Sin validación física de modos.";
        });
    }

    [RelayCommand]
    private async Task SimulateRasterAsync()
    {
        if (IsBusy || PreparedImage is not { } image) { PreparationStatus = "Prepara primero el raster del diseño."; return; }
        if (!int.TryParse(EngravingPower, out var power) || !int.TryParse(EngravingDepth, out var depth)
            || !int.TryParse(EngravingRepeats, out var repeats))
        { PreparationStatus = "Introduce potencia, profundidad y repeticiones explícitas."; return; }
        await RunAsync("Simulando protocolo raster", async token =>
        {
            var raster = await Task.Run(() => RasterProcessor.Prepare(image.Gray, image.Width, image.Height, cancellationToken: token), token);
            var job = EngravingJob.Create(raster, 50 + image.Minimum.X, 50 - image.Maximum.Y, 4,
                new(power, depth, repeats), new("316", AccessoryMode.Fixed, "Sin calibrar", ""));
            await new RasterEngravingSession().RunAsync(() => new SimulatedRasterConnection(), job, null, true,
                new Progress<string>(message => PreparationStatus = message), token);
            PreparationStatus = "Simulación terminada · ningún puerto abierto · grabado real no habilitado";
        });
    }

    [RelayCommand]
    private async Task SimulateAccessoryAsync()
    {
        if (!Number(CommandedTravel, out var commanded) || !Number(MeasuredTravel, out var measured)
            || !Number(DesiredTravel, out var desired) || !Number(AccessoryDiameter, out var diameter)
            || !int.TryParse(TravelDirection, out var direction))
        { AccessorySummary = "Completa las mediciones y el sentido ±1."; return; }
        try
        {
            var mode = (AccessoryMode)(Math.Clamp(AccessoryIndex, 0, 2) + 1);
            var profile = new AccessoryCalibration(mode, commanded, measured, diameter, direction);
            await RunAsync("Simulando accesorio sin hardware", token => AccessorySimulator.RunAsync(profile, desired,
                new Progress<AccessorySimulationState>(state => AccessorySummary =
                    $"SIMULACIÓN · {state.Mode} · superficie {state.SurfaceMm:0.###} mm · comando {state.CommandMm:0.###} mm · {state.Angle:0.###}° · {(state.Finished ? "terminado" : "en curso")}"), token));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { AccessorySummary = ex.Message; }
    }

    [RelayCommand]
    private async Task ImportCalibrationAsync()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog { Filter = "Correspondencias medidas JSON|*.json" };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        await RunAsync("Calculando calibración medida", async token =>
        {
            var bytes = await File.ReadAllBytesAsync(dialog.FileName, token);
            if (bytes.Length > 65536) throw new InvalidDataException("Archivo de correspondencias demasiado grande.");
            var input = JsonSerializer.Deserialize<CalibrationMeasurements>(bytes) ?? throw new InvalidDataException("Sin mediciones.");
            if (input.Scene is null || input.Measured is null || input.Scene.Any(p => p.Length != 2) || input.Measured.Any(p => p.Length != 2)
                || string.IsNullOrWhiteSpace(input.Configuration) || string.IsNullOrWhiteSpace(input.Device))
                throw new InvalidDataException("Se necesitan Scene, Measured, Device y Configuration.");
            var fit = await Task.Run(() => AffineCalibration.Fit(input.Scene.Select(p => new Vector2(p[0], p[1])).ToArray(),
                input.Measured.Select(p => new Vector2(p[0], p[1])).ToArray()), token);
            calibration = new(Guid.NewGuid(), input.Device, "316", AccessoryMode.Fixed, input.Configuration, fit.Transform, fit.Rms, DateTimeOffset.UtcNow);
            SetupFingerprint = input.Configuration;
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaserMeshStudio", "calibrations");
            await new CalibrationStore(directory).SaveAsync(calibration, token);
            PreparationStatus = $"Calibración guardada · RMS {fit.Rms:0.####} mm · no valida enfoque ni habilita grabado";
        });
    }
    [RelayCommand]
    private async Task LoadCalibrationAsync()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog { Filter = "Perfil medido JSON|*.json", InitialDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaserMeshStudio", "calibrations") };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        if (!Guid.TryParse(Path.GetFileNameWithoutExtension(dialog.FileName), out var id))
        { PreparationStatus = "El nombre del perfil debe ser su identificador GUID."; return; }
        await RunAsync("Cargando calibración", async token =>
        {
            calibration = await new CalibrationStore(Path.GetDirectoryName(dialog.FileName)!).LoadAsync(id, token);
            SetupFingerprint = calibration.Configuration;
            OnPropertyChanged(nameof(CanEngraveReal));
            PreparationStatus = $"Perfil {calibration.Id} · RMS {calibration.RmsError:0.####} mm · revisa configuración antes de usar";
        });
    }

    [RelayCommand]
    private async Task LoadEngravingValidationAsync()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog { Filter = "Aceptación física documentada JSON|*.json" };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        await RunAsync("Leyendo aceptación física", async token =>
        {
            if (new FileInfo(dialog.FileName).Length > 65536) throw new InvalidDataException("Registro demasiado grande.");
            var profile = JsonSerializer.Deserialize<EngravingProfile>(await File.ReadAllBytesAsync(dialog.FileName, token));
            if (profile is null || !profile.PhysicallyValidated || profile.Firmware != "316" || profile.Mode != AccessoryMode.Fixed)
                throw new InvalidDataException("Se necesita aceptación supervisada documentada del raster fijo en firmware 316.");
            validatedEngravingProfile = profile; OnPropertyChanged(nameof(CanEngraveReal));
            PreparationStatus = CanEngraveReal ? "Aceptación y calibración compatibles · revisa raster y parámetros antes de iniciar"
                : "Aceptación cargada; falta calibración/configuración compatible";
        });
    }

    [RelayCommand]
    private async Task StartRasterAsync()
    {
        if (IsBusy || !CanEngraveReal || PreparedImage is not { } image || validatedEngravingProfile is not { } profile) return;
        if (!int.TryParse(EngravingPower, out var power) || !int.TryParse(EngravingDepth, out var depth)
            || !int.TryParse(EngravingRepeats, out var repeats)) { PreparationStatus = "Parámetros explícitos incompletos."; return; }
        if (DevelopCylinder || !UseMeasuredCalibration) { PreparationStatus = "El perfil aceptado requiere raster fijo con registro medido."; return; }
        if (BeforeHistoryRestore is { } stop) await stop();
        await RunAsync("Ejecutando trabajo raster explícito", async token =>
        {
            var raster = await Task.Run(() => RasterProcessor.Prepare(image.Gray, image.Width, image.Height, cancellationToken: token), token);
            var job = EngravingJob.Create(raster, 50 + image.Minimum.X, 50 - image.Maximum.Y, 4, new(power, depth, repeats), profile,
                minimumZ: image.MinimumZ, maximumZ: image.MaximumZ);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); rasterCancellation = cancellation;
            PhysicalEngravingChanged?.Invoke(true);
            try
            {
                rasterExecution = rasterSession.RunAsync(() => new UsbPreviewConnection(UsbPortName.Trim()), job, calibration, false,
                    new Progress<string>(s => { PreparationStatus = s; DeviceStatus = s; }), cancellation.Token);
                await rasterExecution;
            }
            finally
            {
                rasterExecution = null; rasterCancellation = null; PhysicalEngravingChanged?.Invoke(false);
                if (rasterSession.UnknownState) { DeviceStatus = "Estado del láser desconocido · usa parada física"; OnPropertyChanged(nameof(CanEngraveReal)); }
            }
        });
    }

    internal async Task StopRasterAsync()
    {
        rasterCancellation?.Cancel();
        if (rasterExecution is { } task)
        {
            try { await task; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException)
            { if (ex is not OperationCanceledException) DeviceStatus = ex.Message; }
        }
    }
    private sealed record CalibrationMeasurements(float[][] Scene, float[][] Measured, string Device, string Configuration);
}
