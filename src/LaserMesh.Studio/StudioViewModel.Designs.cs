using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaserMesh.Geometry;
using LaserMesh.Imaging;
using LaserMesh.Projects;
using Microsoft.Win32;

namespace LaserMesh.Studio;

internal sealed partial class StudioViewModel
{
    internal event Action? DesignsChanged;
    public IReadOnlyList<SurfaceDesign> Designs => sceneMetadata.Designs;
    [ObservableProperty] public partial SurfaceDesign? SelectedDesign { get; set; }
    [ObservableProperty] public partial string DesignWidth { get; set; } = "30";
    [ObservableProperty] public partial string DesignHeight { get; set; } = "30";
    [ObservableProperty] public partial string DesignAngle { get; set; } = "0";
    [ObservableProperty] public partial string DesignOpacity { get; set; } = "1";
    [ObservableProperty] public partial string DesignX { get; set; } = "0";
    [ObservableProperty] public partial string DesignY { get; set; } = "0";
    [ObservableProperty] public partial string DesignZ { get; set; } = "0";
    [ObservableProperty] public partial string CylinderRadius { get; set; } = "10";
    [ObservableProperty] public partial string CylinderSeam { get; set; } = "0";
    [ObservableProperty] public partial bool LockDesignAspect { get; set; } = true;
    [ObservableProperty] public partial string DesignNormalX { get; set; } = "0";
    [ObservableProperty] public partial string DesignNormalY { get; set; } = "0";
    [ObservableProperty] public partial string DesignNormalZ { get; set; } = "1";
    [ObservableProperty] public partial string CropLeft { get; set; } = "0";
    [ObservableProperty] public partial string CropTop { get; set; } = "0";
    [ObservableProperty] public partial string CropRight { get; set; } = "1";
    [ObservableProperty] public partial string CropBottom { get; set; } = "1";
    [ObservableProperty] public partial bool ShowUvDistortion { get; set; }
    [ObservableProperty] public partial int DesignMapping { get; set; }
    [ObservableProperty] public partial string SelectedChart { get; set; } = "0";
    [ObservableProperty] public partial string ChartX { get; set; } = "0";
    [ObservableProperty] public partial string ChartY { get; set; } = "0";
    [ObservableProperty] public partial string ChartScale { get; set; } = "1";
    [ObservableProperty] public partial string ChartAngle { get; set; } = "0";
    private bool syncingDesign;
    private bool pendingDesign;
    private readonly DispatcherTimer designTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    internal ProjectedImage? PreparedImage { get; private set; }
    public ImageSource? RasterPreview { get; private set; }
    public string DesignSummary => SelectedDesign is { } design ? $"{design.Name} · {design.Projection}" : "Sin diseño seleccionado";
    internal void InvalidatePreparedImage()
    {
        PreparedImage = null; RasterPreview = null; OnPropertyChanged(nameof(RasterPreview));
    }

    partial void OnSelectedDesignChanged(SurfaceDesign? value)
    {
        if (syncingDesign) return;
        syncingDesign = true;
        try
        {
            if (value is null) return;
            DesignWidth = F(value.Width); DesignHeight = F(value.Height); DesignAngle = F(value.Angle); DesignOpacity = F(value.Opacity);
            DesignX = F(value.Origin.X); DesignY = F(value.Origin.Y); DesignZ = F(value.Origin.Z);
            DesignNormalX = F(value.Normal.X); DesignNormalY = F(value.Normal.Y); DesignNormalZ = F(value.Normal.Z);
            CropLeft = F(value.Crop.X); CropTop = F(value.Crop.Y); CropRight = F(value.Crop.Z); CropBottom = F(value.Crop.W);
            CylinderRadius = F(value.CylinderRadius); CylinderSeam = F(value.SeamDegrees); DesignMapping = (int)value.Projection;
            OnPropertyChanged(nameof(DesignSummary)); DesignsChanged?.Invoke();
        }
        finally { syncingDesign = false; }
    }
    private static string F(double value) => value.ToString("G9", CultureInfo.CurrentCulture);
    private static bool Number(string value, out double result) => double.TryParse(value, NumberStyles.Float,
        CultureInfo.CurrentCulture, out result) && double.IsFinite(result);
    partial void OnDesignWidthChanged(string value)
    {
        if (!syncingDesign && LockDesignAspect && SelectedDesign is { } design && Number(value, out var width) && width > 0)
        { syncingDesign = true; DesignHeight = F(width * design.Height / design.Width); syncingDesign = false; }
        EditDesign();
    }
    partial void OnDesignHeightChanged(string value)
    {
        if (!syncingDesign && LockDesignAspect && SelectedDesign is { } design && Number(value, out var height) && height > 0)
        { syncingDesign = true; DesignWidth = F(height * design.Width / design.Height); syncingDesign = false; }
        EditDesign();
    }
    partial void OnDesignAngleChanged(string value) => EditDesign();
    partial void OnDesignOpacityChanged(string value) => EditDesign();
    partial void OnDesignXChanged(string value) => EditDesign();
    partial void OnDesignYChanged(string value) => EditDesign();
    partial void OnDesignZChanged(string value) => EditDesign();
    partial void OnCylinderRadiusChanged(string value) => EditDesign();
    partial void OnCylinderSeamChanged(string value) => EditDesign();
    partial void OnDesignNormalXChanged(string value) => EditDesign();
    partial void OnDesignNormalYChanged(string value) => EditDesign();
    partial void OnDesignNormalZChanged(string value) => EditDesign();
    partial void OnCropLeftChanged(string value) => EditDesign();
    partial void OnCropTopChanged(string value) => EditDesign();
    partial void OnCropRightChanged(string value) => EditDesign();
    partial void OnCropBottomChanged(string value) => EditDesign();
    partial void OnShowUvDistortionChanged(bool value) => DesignsChanged?.Invoke();
    partial void OnDesignMappingChanged(int value) => EditDesign();
    partial void OnChartXChanged(string value) => EditDesign();
    partial void OnChartYChanged(string value) => EditDesign();
    partial void OnChartScaleChanged(string value) => EditDesign();
    partial void OnChartAngleChanged(string value) => EditDesign();
    partial void OnSelectedChartChanged(string value) => DesignsChanged?.Invoke();

    private void EditDesign()
    {
        if (syncingDesign || IsBusy || SelectedDesign is not { } design) return;
        if (!Number(DesignWidth, out var width) || !Number(DesignHeight, out var height) || width <= 0 || height <= 0
            || !Number(DesignAngle, out var angle) || !Number(DesignOpacity, out var opacity) || opacity is < 0 or > 1
            || !Number(DesignX, out var x) || !Number(DesignY, out var y) || !Number(DesignZ, out var z)
            || !Number(DesignNormalX, out var nx) || !Number(DesignNormalY, out var ny) || !Number(DesignNormalZ, out var nz)
            || nx * nx + ny * ny + nz * nz < 0.0001 || Math.Abs(nx) + Math.Abs(ny) + Math.Abs(nz) > 1e6
            || !Number(CropLeft, out var left) || !Number(CropTop, out var top) || !Number(CropRight, out var right) || !Number(CropBottom, out var bottom)
            || left < 0 || top < 0 || right > 1 || bottom > 1 || right <= left || bottom <= top
            || !Number(CylinderRadius, out var radius) || radius <= 0 || !Number(CylinderSeam, out var seam)
            || !Number(ChartX, out var chartX) || !Number(ChartY, out var chartY) || !Number(ChartScale, out var chartScale) || chartScale <= 0
            || !Number(ChartAngle, out var chartAngle) || !int.TryParse(SelectedChart, out var chart) || chart < 0)
        { Status = "Introduce valores válidos para el diseño."; return; }
        if (DesignMapping == 2 && design.AtlasId is null) { Status = "Genera primero el atlas UV automático."; return; }
        var updated = design with { Width = width, Height = height, Angle = angle, Opacity = opacity, Origin = new((float)x, (float)y, (float)z),
            Normal = Vector3.Normalize(new((float)nx, (float)ny, (float)nz)), Crop = new((float)left, (float)top, (float)right, (float)bottom),
            Projection = (DesignProjection)Math.Clamp(DesignMapping, 0, 2), CylinderRadius = radius, SeamDegrees = seam,
            Islands = [.. design.Islands.Where(i => i.Chart != chart), new(chart, new((float)chartX, (float)chartY), chartScale, chartAngle)] };
        sceneMetadata = sceneMetadata with { Designs = Designs.Select(d => d.Id == updated.Id ? updated : d).ToArray() };
        syncingDesign = true; try { SelectedDesign = updated; } finally { syncingDesign = false; }
        PreparedImage = null; RasterPreview = null; OnPropertyChanged(nameof(RasterPreview));
        UpdateDocument(projectPath, true); OnPropertyChanged(nameof(Designs)); OnPropertyChanged(nameof(DesignSummary)); DesignsChanged?.Invoke();
        pendingDesign = true; designTimer.Stop(); designTimer.Start();
    }
    private void CommitDesignEdit()
    { designTimer.Stop(); if (pendingDesign) { pendingDesign = false; Commit("Editar diseño de superficie"); } }

    [RelayCommand]
    private async Task AddImageAsync()
    {
        if (IsBusy || Selected is null) { Status = "Selecciona una pieza destino."; return; }
        var dialog = new OpenFileDialog { Filter = "Imagen PNG/JPEG|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog(Application.Current.MainWindow) == true) await AddImageFileAsync(dialog.FileName, Selected.Model.Id);
    }

    internal Task AddImageFileAsync(string path, Guid modelId, Vector3? pickedOrigin = null, Vector3? pickedNormal = null)
        => RunAsync("Añadiendo imagen a superficie", async token =>
        {
            var model = Models.Single(m => m.Model.Id == modelId).Model;
            if (new FileInfo(path).Length is <= 0 or > 32 * 1024 * 1024) throw new InvalidDataException("Imagen limitada a 32 MiB.");
            var bytes = await File.ReadAllBytesAsync(path, token);
            var decoded = await Task.Run(() => DecodeImage(bytes), token);
            var asset = new DesignAsset(Guid.NewGuid(), Path.GetFileName(path), Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png" : "image/jpeg", bytes);
            var origin = pickedOrigin ?? (model.Mesh.Centroid with { Z = model.Mesh.Maximum.Z });
            var design = new SurfaceDesign(Guid.NewGuid(), asset.Id, modelId, origin, pickedNormal ?? Vector3.UnitZ,
                30, 30d * decoded.Height / decoded.Width, 0, 1, DesignProjection.Planar) { Name = asset.Name };
            sceneMetadata = sceneMetadata with { Assets = [.. sceneMetadata.Assets, asset], Designs = [.. Designs, design] };
            SelectedDesign = design; OnPropertyChanged(nameof(Designs)); Commit("Añadir imagen"); DesignsChanged?.Invoke();
            Status = "Imagen añadida · sin emisión";
        });

    [RelayCommand]
    private void RemoveDesign()
    {
        if (IsBusy || SelectedDesign is not { } selected) return;
        CommitDesignEdit(); sceneMetadata = sceneMetadata with { Designs = Designs.Where(d => d.Id != selected.Id).ToArray() };
        SelectedDesign = null; OnPropertyChanged(nameof(Designs)); Commit("Quitar diseño"); DesignsChanged?.Invoke();
    }

    [RelayCommand]
    private async Task GenerateUvAsync()
    {
        if (IsBusy || SelectedDesign is not { } design) return;
        await RunAsync("Desplegando UV con xatlas", async token =>
        {
            var model = Models.Single(m => m.Model.Id == design.ModelId);
            var atlas = await new XatlasUnwrapper().UnwrapAsync(model.Model.Mesh, token);
            var asset = new DesignAsset(Guid.NewGuid(), "Atlas UV", "application/x-lasermesh-uv", atlas.Encode());
            var updated = design with { AtlasId = asset.Id, Projection = DesignProjection.Uv };
            sceneMetadata = sceneMetadata with { Assets = [.. sceneMetadata.Assets, asset], Designs = Designs.Select(d => d.Id == design.Id ? updated : d).ToArray() };
            SelectedDesign = updated; OnPropertyChanged(nameof(Designs)); Commit("Generar atlas UV"); DesignsChanged?.Invoke();
            Status = $"UV listo · {atlas.Charts.Distinct().Count()} islas · geometría original conservada";
        });
    }

    [RelayCommand]
    private async Task PrepareRasterAsync()
    {
        if (IsBusy || Designs.Count == 0) return;
        CommitDesignEdit(); var snapshot = CaptureDocument();
        var develop = DevelopCylinder; var designId = SelectedDesign?.Id;
        if (develop && designId is null) { PreparationStatus = "Selecciona un diseño para desarrollar el cilindro."; return; }
        if (develop && UseMeasuredCalibration) { PreparationStatus = "El registro fijo XY no calibra el accesorio rotatorio; desactívalo para desarrollar."; return; }
        var registration = System.Numerics.Matrix4x4.Identity;
        if (UseMeasuredCalibration)
        {
            if (calibration is null || !calibration.Matches(BluetoothName, "316", LaserMesh.Calibration.AccessoryMode.Fixed, SetupFingerprint))
            { PreparationStatus = "La calibración no coincide con dispositivo, firmware o configuración actual."; return; }
            registration = calibration.Transform;
        }
        await RunAsync("Componiendo superficies visibles", async token =>
        {
            var prepared = await Task.Run(() =>
            {
                var decoded = snapshot.Assets.Where(a => a.MediaType.StartsWith("image/", StringComparison.Ordinal))
                    .ToDictionary(a => a.Id, a => DecodeImage(a.Content));
                return develop && designId is { } id ? CylindricalDevelopment.Compose(snapshot, id, decoded, token)
                    : SurfaceCompositor.Compose(snapshot, decoded, 10, token, registration);
            }, token);
            PreparedImage = prepared;
            var bitmap = BitmapSource.Create(prepared.Width, prepared.Height, 96, 96, PixelFormats.Gray8, null, prepared.Gray, prepared.Width);
            bitmap.Freeze(); RasterPreview = bitmap; OnPropertyChanged(nameof(RasterPreview));
            Status = $"Raster {prepared.Width} × {prepared.Height} · Z {prepared.MinimumZ:0.##}…{prepared.MaximumZ:0.##} mm · enfoque físico sin validar";
        });
    }

    [RelayCommand]
    private async Task ExportRasterAsync()
    {
        if (RasterPreview is not BitmapSource bitmap) return;
        var dialog = new SaveFileDialog { Filter = "Imagen preparada PNG|*.png", DefaultExt = ".png" };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        await RunAsync("Exportando raster", token => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(dialog.FileName); encoder.Save(stream);
        }, token));
    }

    private SurfaceDesign? designGesture;
    internal void BeginDesignGesture() { CommitDesignEdit(); designGesture = SelectedDesign; }
    internal void UpdateDesignGesture(Vector3 localDelta)
    {
        if (designGesture is not { } before) return;
        var design = before with { Origin = before.Origin + localDelta };
        sceneMetadata = sceneMetadata with { Designs = Designs.Select(d => d.Id == design.Id ? design : d).ToArray() };
        syncingDesign = true; try { SelectedDesign = design; } finally { syncingDesign = false; }
        UpdateDocument(projectPath, true); DesignsChanged?.Invoke();
    }
    internal void EndDesignGesture(bool cancel)
    {
        if (designGesture is not { } before) return;
        designGesture = null;
        if (cancel) sceneMetadata = sceneMetadata with { Designs = Designs.Select(d => d.Id == before.Id ? before : d).ToArray() };
        else Commit("Mover diseño sobre superficie");
        SelectedDesign = null; SelectedDesign = Designs.FirstOrDefault(d => d.Id == before.Id);
        OnPropertyChanged(nameof(Designs)); DesignsChanged?.Invoke();
    }

    internal static ImagePixels DecodeImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (frame.PixelWidth is <= 0 or > 8192 || frame.PixelHeight is <= 0 or > 8192 || (long)frame.PixelWidth * frame.PixelHeight > 16_777_216)
            throw new InvalidDataException("Imagen limitada a 16 megapíxeles.");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4]; converted.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        return new(frame.PixelWidth, frame.PixelHeight, pixels);
    }
}
