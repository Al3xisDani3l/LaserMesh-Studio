using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.SharpDX;
using LaserMesh.Engraving;
using LaserMesh.Geometry;
using LaserMesh.Hardware.LaserPecker.Bluetooth;
using LaserMesh.Projects;
using LaserMesh.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace LaserMesh.Studio;

internal sealed record SceneItem(ProjectModel Model, MeshGeometry3D Geometry)
{
    public string Name => Model.Name;
    public string Dimensions => Format(Model.Mesh.Dimensions);
    public string Bounds => $"{Format(Model.Mesh.Minimum)} — {Format(Model.Mesh.Maximum)}";
    public string Centroid => Format(Model.Mesh.Centroid);
    public string Counts => $"{Model.Mesh.VertexCount:N0} vértices · {Model.Mesh.TriangleCount:N0} caras";

    private static string Format(System.Numerics.Vector3 v) => $"{v.X:G6} × {v.Y:G6} × {v.Z:G6}";
}

internal sealed partial class StudioViewModel : ObservableObject
{
    private readonly StlCodec stl;
    private readonly ProjectStore store;
    private readonly MeshAdapter meshAdapter;
    private readonly RecentProjects recents;
    private readonly ILogger<StudioViewModel> log;
    private CancellationTokenSource? operation;
    private string? projectPath;

    public StudioViewModel(StlCodec stl, ProjectStore store, MeshAdapter meshAdapter, RecentProjects recents,
        GCodeCodec gcode, LaserPeckerBluetooth bluetooth, ILogger<StudioViewModel> log)
    {
        this.stl = stl;
        this.store = store;
        this.meshAdapter = meshAdapter;
        this.recents = recents;
        this.gcode = gcode;
        this.bluetooth = bluetooth;
        this.log = log;
        try
        {
            RecentPaths = recents.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.LogWarning(ex, "Could not load recent projects");
        }
    }

    public event Action<bool>? SceneChanged;

    [ObservableProperty] public partial IReadOnlyList<SceneItem> Models { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> RecentPaths { get; private set; } = [];
    [ObservableProperty] public partial SceneItem? Selected { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = "Listo para importar un STL.";
    [ObservableProperty] public partial string DocumentTitle { get; private set; } = "Proyecto sin título";
    [ObservableProperty] public partial int UnitIndex { get; set; }
    [ObservableProperty] public partial string X { get; set; } = "0";
    [ObservableProperty] public partial string Y { get; set; } = "0";
    [ObservableProperty] public partial string Z { get; set; } = "0";
    [ObservableProperty] public partial string Rotation { get; set; } = "0";
    [ObservableProperty] public partial string Scale { get; set; } = "1";

    private bool IsDirty { get; set; }

    partial void OnSelectedChanged(SceneItem? value)
    {
        var pose = value?.Model.Pose ?? ModelPose.Identity;
        X = pose.X.ToString(CultureInfo.CurrentCulture);
        Y = pose.Y.ToString(CultureInfo.CurrentCulture);
        Z = pose.Z.ToString(CultureInfo.CurrentCulture);
        Rotation = pose.Rotation.ToString(CultureInfo.CurrentCulture);
        Scale = pose.Scale.ToString(CultureInfo.CurrentCulture);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (IsBusy)
            return;
        var dialog = new OpenFileDialog { Filter = "Malla STL (*.stl)|*.stl", Title = "Importar malla STL" };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        var factor = UnitIndex switch { 1 => 10f, 2 => 1000f, 3 => 25.4f, _ => 1f };
        await ImportFileAsync(dialog.FileName, factor);
    }

    internal Task ImportFileAsync(string path, float factor)
        => RunAsync("Importando STL", async token =>
        {
            if (Models.Count >= ProjectStore.MaxModels)
                throw new InvalidDataException("Se alcanzó el límite de modelos por proyecto.");
            var mesh = await stl.ImportAsync(path, factor, token);
            if (Models.Sum(x => x.Model.Mesh.TriangleCount) + mesh.TriangleCount > ProjectStore.MaxTriangles)
                throw new InvalidDataException("Se alcanzó el límite de triángulos por proyecto.");
            var geometry = await meshAdapter.PrepareAsync(mesh, token);
            var item = new SceneItem(new(Guid.NewGuid(), Path.GetFileNameWithoutExtension(path), mesh, ModelPose.Identity), geometry);
            Models = [.. Models, item];
            Selected = item;
            UpdateDocument(projectPath, true);
            SceneChanged?.Invoke(true);
            log.LogInformation("Imported {Path} with {TriangleCount} triangles and scale {UnitScale}",
                path, mesh.TriangleCount, factor);
            Status = $"Importado · {item.Name}";
        });
    [RelayCommand]
    private Task NewAsync() => ResetAsync("Proyecto nuevo.");

    [RelayCommand]
    private Task CloseProjectAsync() => ResetAsync("Sin proyecto abierto.");

    private async Task ResetAsync(string message)
    {
        if (IsBusy || !await ConfirmDiscardAsync())
            return;
        Models = [];
        Selected = null;
        ClearToolpath();
        UpdateDocument(null, false);
        SceneChanged?.Invoke(true);
        Status = message;
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (IsBusy)
            return;
        var dialog = new OpenFileDialog { Filter = "Proyecto LaserMesh (*.lmeshproj)|*.lmeshproj" };
        if (dialog.ShowDialog(Application.Current.MainWindow) == true)
            await OpenRecentAsync(dialog.FileName);
    }

    [RelayCommand]
    internal async Task OpenRecentAsync(string? path)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(path) || !await ConfirmDiscardAsync())
            return;

        await RunAsync("Abriendo proyecto", async token =>
        {
            var models = await store.OpenAsync(path, token);
            var prepared = new List<SceneItem>(models.Count);
            foreach (var model in models)
                prepared.Add(new(model, await meshAdapter.PrepareAsync(model.Mesh, token)));

            Models = prepared;
            Selected = Models.FirstOrDefault();
            ClearToolpath();
            UpdateDocument(path, false);
            SceneChanged?.Invoke(true);
            await RecordRecentAsync(path);
            log.LogInformation("Opened project {Path} with {ModelCount} models", path, Models.Count);
            Status = $"Abierto · {Path.GetFileName(path)}";
        });
    }

    [RelayCommand]
    private Task SaveAsync() => SaveDocumentAsync(false);

    [RelayCommand]
    private Task SaveAsAsync() => SaveDocumentAsync(true);

    private async Task<bool> SaveDocumentAsync(bool saveAs)
    {
        if (IsBusy)
            return false;

        var path = projectPath;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Proyecto LaserMesh (*.lmeshproj)|*.lmeshproj",
                DefaultExt = ".lmeshproj",
                AddExtension = true,
                FileName = path is null ? "Proyecto.lmeshproj" : Path.GetFileName(path)
            };
            if (dialog.ShowDialog(Application.Current.MainWindow) != true)
                return false;
            path = dialog.FileName;
        }

        return await RunAsync("Guardando proyecto", async token =>
        {
            await store.SaveAsync(path, Models.Select(x => x.Model).ToArray(), token);
            UpdateDocument(path, false);
            await RecordRecentAsync(path);
            log.LogInformation("Saved project {Path} with {ModelCount} models", path, Models.Count);
            Status = $"Guardado · {Path.GetFileName(path)}";
        });
    }

    [RelayCommand]
    private void ApplyPose()
    {
        if (IsBusy || Selected is not { } selected)
            return;
        if (!double.TryParse(X, NumberStyles.Float, CultureInfo.CurrentCulture, out var x)
            || !double.TryParse(Y, NumberStyles.Float, CultureInfo.CurrentCulture, out var y)
            || !double.TryParse(Z, NumberStyles.Float, CultureInfo.CurrentCulture, out var z)
            || !double.TryParse(Rotation, NumberStyles.Float, CultureInfo.CurrentCulture, out var rotation)
            || !double.TryParse(Scale, NumberStyles.Float, CultureInfo.CurrentCulture, out var scale))
        {
            Status = "Introduce valores numéricos para la transformación.";
            return;
        }

        var pose = new ModelPose(x, y, z, rotation, scale);
        if (!pose.IsValid())
        {
            Status = "Transformación fuera de rango. Giro de ±360° y escala de 0.0001 a 1000.";
            return;
        }
        if (pose == selected.Model.Pose)
            return;

        var updated = selected with { Model = selected.Model with { Pose = pose } };
        Models = Models.Select(item => item == selected ? updated : item).ToArray();
        Selected = updated;
        UpdateDocument(projectPath, true);
        SceneChanged?.Invoke(false);
        Status = $"Transformado · {updated.Name}";
    }

    [RelayCommand]
    private void Remove()
    {
        if (IsBusy || Selected is not { } selected)
            return;
        Models = Models.Where(x => x != selected).ToArray();
        Selected = Models.FirstOrDefault();
        UpdateDocument(projectPath, true);
        SceneChanged?.Invoke(false);
        Status = $"Quitado · {selected.Name}";
    }

    [RelayCommand]
    private void Cancel() => operation?.Cancel();

    internal async Task<bool> ConfirmDiscardAsync()
    {
        if (IsBusy)
        {
            Status = "Espera a que termine la operación o cancélala.";
            return false;
        }
        if (!IsDirty)
            return true;

        var choice = MessageBox.Show(Application.Current.MainWindow, "¿Guardar los cambios del proyecto?",
            "LaserMesh Studio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || (choice == MessageBoxResult.Yes && await SaveDocumentAsync(false));
    }

    private void UpdateDocument(string? path, bool dirty)
    {
        projectPath = path;
        IsDirty = dirty;
        DocumentTitle = (path is null ? "Proyecto sin título" : Path.GetFileNameWithoutExtension(path))
            + (dirty ? "  *" : "");
    }

    private async Task RecordRecentAsync(string path)
    {
        RecentPaths = new[] { path }.Concat(RecentPaths).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        var snapshot = RecentPaths;
        try
        {
            await Task.Run(() => recents.Save(snapshot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not save recent projects");
        }
    }

    private async Task<bool> RunAsync(string action, Func<CancellationToken, Task> work)
    {
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        IsBusy = true;
        Status = $"{action}…";
        try
        {
            await work(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = "Operación cancelada.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException
            or InvalidDataException or TimeoutException)
        {
            log.LogWarning(ex, "Failed to execute {Operation}", action);
            Status = $"Error · {ex.Message}";
            MessageBox.Show(Application.Current.MainWindow, ex.Message, action, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            operation = null;
            IsBusy = false;
        }
    }
}
