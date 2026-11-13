using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX;
using LaserMesh.Engraving;
using LaserMesh.Hardware.LaserPecker.Bluetooth;
using LaserMesh.Rendering;
using LaserMesh.Projects;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace LaserMesh.Studio;

internal sealed partial class StudioViewModel
{
    private readonly GCodeCodec gcode;
    private readonly LaserPeckerBluetooth bluetooth;

    public event Action? FieldChanged;
    public event Action? ToolpathChanged;
    public event Action? MoveRequested;

    public EngravingField Field { get; private set; } = new(EngravingField.NominalHeight);
    public Toolpath? Toolpath { get; private set; }
    public LineGeometry3D? TravelGeometry { get; private set; }
    public LineGeometry3D? BurnGeometry { get; private set; }

    [ObservableProperty] public partial bool ShowField { get; set; } = true;
    [ObservableProperty] public partial string FieldHeight { get; set; } = "110";
    [ObservableProperty] public partial string FieldSummary { get; private set; } = "100 × 100 mm nominal";
    [ObservableProperty] public partial string BluetoothName { get; set; } = "LaserPecker-II103592";
    [ObservableProperty] public partial string BluetoothStatus { get; private set; } = "BLE sin consultar · simulación";
    [ObservableProperty] public partial bool HasToolpath { get; private set; }
    [ObservableProperty] public partial string ToolpathSummary { get; private set; } = "Sin trayectoria";

    partial void OnFieldHeightChanged(string value) => ApplyField();

    partial void OnShowFieldChanged(bool value) => FieldChanged?.Invoke();

    partial void OnBluetoothNameChanged(string value) => BluetoothStatus = "BLE sin consultar · simulación";

    [RelayCommand]
    private void ApplyField()
    {
        if (!double.TryParse(FieldHeight, NumberStyles.Float, CultureInfo.CurrentCulture, out var height)
            || !double.IsFinite(height) || height is < 1 or > 1000)
        {
            Status = "Introduce una altura de campo entre 1 y 1000 mm.";
            return;
        }
        Field = new(height);
        var width = Field.PlaneWidth.ToString("0.#", CultureInfo.CurrentCulture);
        FieldSummary = height == EngravingField.NominalHeight
            ? $"{width} × {width} mm nominal" : $"{width} × {width} mm geométricos · fuera de foco";
        FieldChanged?.Invoke();
    }

    [RelayCommand]
    private async Task ProbeBluetoothAsync()
    {
        if (IsBusy)
            return;
        var name = BluetoothName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Status = "Escribe el nombre Bluetooth anunciado por el LaserPecker.";
            return;
        }

        BluetoothStatus = "BLE consultando…";
        var success = await RunAsync($"Buscando {name}", async token =>
        {
            var (state, firmware) = await bluetooth.ProbeAsync(name, token);
            if (string.Equals(name, BluetoothName.Trim(), StringComparison.OrdinalIgnoreCase))
                BluetoothStatus = $"BLE verificado · FW {firmware}";
            Status = $"{name} · modo {state.Mode} · error {state.Error} · sesión cerrada";
            log.LogInformation("LP2 BLE probe for {Device} returned firmware {Firmware}, mode {Mode}, error {Error}",
                name, firmware, state.Mode, state.Error);
        });
        if (!success && string.Equals(name, BluetoothName.Trim(), StringComparison.OrdinalIgnoreCase))
            BluetoothStatus = "BLE sin verificar · simulación";
    }

    [RelayCommand]
    private async Task OpenToolpathAsync()
    {
        if (IsBusy)
            return;
        var dialog = new OpenFileDialog { Filter = "Programa G-code (*.gcode;*.nc;*.tap)|*.gcode;*.nc;*.tap" };
        if (dialog.ShowDialog(Application.Current.MainWindow) == true)
            await OpenToolpathFileAsync(dialog.FileName);
    }

    internal Task OpenToolpathFileAsync(string path)
        => RunAsync("Preparando trayectoria", async token =>
        {
            var toolpath = await gcode.OpenAsync(path, token);
            var (travel, burn) = await EngravingGeometry.CreateToolpathAsync(toolpath, token);
            Toolpath = toolpath;
            TravelGeometry = travel;
            BurnGeometry = burn;
            HasToolpath = true;
            ToolpathSummary = $"{toolpath.Segments.Count:N0} segmentos · {toolpath.DurationSeconds:N1} s estimados";
            ToolpathChanged?.Invoke();
            SceneChanged?.Invoke(true);
            Status = $"Simulación · {Path.GetFileName(path)}";
        });

    internal async Task ExportPlacementAsync(string path, PlacementGuide guide)
    {
        if (IsBusy)
            return;

        await RunAsync("Exportando guía", async token =>
        {
            var program = await Task.Run(() => guide.ToToolpath(token), token);
            await gcode.SaveAsync(path, program, token);
            Status = $"Guía exportada sin emisión · {Path.GetFileName(path)}";
        });
    }

    [RelayCommand]
    private async Task ExportToolpathAsync()
    {
        if (IsBusy || Toolpath is not { } toolpath)
            return;
        var dialog = new SaveFileDialog { Filter = "Programa G-code (*.gcode)|*.gcode", DefaultExt = ".gcode",
            FileName = "Trayectoria.gcode" };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;
        await RunAsync("Exportando G-code", async token =>
        {
            await gcode.SaveAsync(dialog.FileName, toolpath, token);
            Status = $"Exportado para revisión · {Path.GetFileName(dialog.FileName)}";
        });
    }

    [RelayCommand]
    private void RemoveToolpath()
    {
        if (!IsBusy)
            ClearToolpath();
    }

    private void ClearToolpath()
    {
        Toolpath = null;
        TravelGeometry = null;
        BurnGeometry = null;
        HasToolpath = false;
        ToolpathSummary = "Sin trayectoria";
        ToolpathChanged?.Invoke();
    }

    [RelayCommand]
    private void Duplicate() => DuplicateSelection();

    [RelayCommand]
    private void Move()
    {
        if (IsBusy || Selected is null)
            return;
        Status = "Arrastra la selección en XY o edita sus valores en vivo.";
        MoveRequested?.Invoke();
    }
}
