using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using LaserMesh.Projects;

namespace LaserMesh.Studio;

internal sealed partial class StudioViewModel
{
    internal SelectionState Selection { get; } = new();
    private readonly EditHistory history = new();
    private SceneDocument sceneMetadata = new();
    private bool syncingFields;
    private bool syncingSelection;
    private bool numericEditing;
    private readonly DispatcherTimer numericTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private SceneItem[]? gestureItems;
    internal Func<Task>? BeforeHistoryRestore { get; set; }
    internal event Action? SelectionChanged;
    internal IReadOnlyList<SceneItem> SelectedItems => Models.Where(m => Selection.Ids.Contains(m.Model.Id)).ToArray();
    public string SelectionSummary => $"{Selection.Ids.Count} pieza(s) seleccionada(s)";
    public string WorldDimensions => Selected is null ? "" : Format(Bounds(SelectedItems).Max - Bounds(SelectedItems).Min);
    public string UndoDescription => "Deshacer " + history.UndoName;
    public bool CanUndo => history.CanUndo;
    public bool CanRedo => history.CanRedo;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] public partial int PivotIndex { get; set; }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] public partial string RenameText { get; set; } = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] public partial string SupportHeight { get; set; } = "0";
    internal Func<PreparationSettings>? PreparationSnapshot { get; set; }
    internal event Action<PreparationSettings>? PreparationRestored;
    internal SceneDocument CaptureDocument() => sceneMetadata with { Models = Models.Select(m => m.Model).ToArray(),
        Preparation = PreparationSnapshot?.Invoke() ?? sceneMetadata.Preparation };
    internal void RecordPreparationChange() { if (!IsBusy) Commit("Cambiar referencia o guía"); }

    internal void SelectItems(IEnumerable<Guid> ids, Guid? active = null)
    {
        CommitNumericEdit();
        Selection.Replace(ids.Where(id => Models.Any(m => m.Model.Id == id && m.Model.Visible)), active);
        syncingSelection = true;
        try { Selected = Models.FirstOrDefault(m => m.Model.Id == Selection.ActiveId); }
        finally { syncingSelection = false; }
        RefreshInspector(); SelectionChanged?.Invoke();
    }

    internal void SelectHit(SceneItem? item, bool toggle)
    {
        if (item is null) { if (!toggle) SelectItems([]); return; }
        var ids = item.Model.GroupId is { } group
            ? Models.Where(m => m.Model.GroupId == group && m.Model.Visible).Select(m => m.Model.Id).ToArray()
            : new[] { item.Model.Id };
        var selected = Selection.Ids.ToHashSet();
        if (!toggle) selected.Clear();
        if (toggle && ids.All(selected.Contains)) selected.ExceptWith(ids); else selected.UnionWith(ids);
        SelectItems(selected, item.Model.Id);
    }

    private void RefreshInspector()
    {
        syncingFields = true;
        try
        {
            var pose = Selected?.Model.Pose ?? ModelPose.Identity;
            X = pose.X.ToString("0.###", CultureInfo.CurrentCulture); Y = pose.Y.ToString("0.###", CultureInfo.CurrentCulture);
            Z = pose.Z.ToString("0.###", CultureInfo.CurrentCulture); Rotation = pose.EulerDegrees.Z.ToString("0.###", CultureInfo.CurrentCulture);
            RotationX = pose.EulerDegrees.X.ToString("0.###", CultureInfo.CurrentCulture); RotationY = pose.EulerDegrees.Y.ToString("0.###", CultureInfo.CurrentCulture);
            Scale = pose.Scale.ToString("0.####", CultureInfo.CurrentCulture);
            OnPropertyChanged(nameof(SelectionSummary)); OnPropertyChanged(nameof(WorldDimensions));
        }
        finally { syncingFields = false; }
    }

    partial void OnXChanged(string value) => EditNumeric();
    partial void OnYChanged(string value) => EditNumeric();
    partial void OnZChanged(string value) => EditNumeric();
    partial void OnRotationChanged(string value) => EditNumeric();
    partial void OnRotationXChanged(string value) => EditNumeric();
    partial void OnRotationYChanged(string value) => EditNumeric();
    partial void OnScaleChanged(string value) => EditNumeric();

    private void EditNumeric()
    {
        if (syncingFields || IsBusy || Selected is null) return;
        numericEditing = true;
        ApplyPose();
        numericTimer.Stop(); numericTimer.Start();
    }

    internal void CommitNumericEdit()
    {
        CommitDesignEdit();
        numericTimer.Stop();
        if (!numericEditing) return;
        numericEditing = false;
        if (numericBefore is not null) { Commit("Editar transformación"); SceneChanged?.Invoke(false); }
    }

    internal void CancelNumericEdit()
    {
        numericTimer.Stop(); numericEditing = false;
        if (numericBefore is { } before) ApplyItems(before);
        numericBefore = null;
        RefreshInspector();
    }
    private SceneItem[]? numericBefore;
    internal bool IsEditingNumbers => numericEditing;

    private void ApplyInspectorPose(ModelPose before, ModelPose after)
    {
        numericBefore ??= Models.ToArray();
        var pivot = SelectionPivot;
        var beforeRotation = Matrix4x4.CreateFromQuaternion(before.EffectiveOrientation);
        if (!Matrix4x4.Invert(beforeRotation, out var inverse)) return;
        var delta = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateScale((float)(after.Scale / before.Scale))
            * inverse * Matrix4x4.CreateFromQuaternion(after.EffectiveOrientation) * Matrix4x4.CreateTranslation(pivot)
            * Matrix4x4.CreateTranslation((float)(after.X - before.X), (float)(after.Y - before.Y), (float)(after.Z - before.Z));
        var items = Models.Select(item => Selection.Ids.Contains(item.Model.Id) && !item.Model.Locked
            ? item with { Model = item.Model with { Pose = item.Model.Pose.Transform(InstanceDelta(item, delta, pivot)) } } : item).ToArray();
        ApplyItems(items);
        if (!numericEditing) Commit("Transformar selección");
    }

    private void ApplyItems(IReadOnlyList<SceneItem> items)
    {
        InvalidatePreparedImage();
        Models = items;
        syncingSelection = true;
        try { Selected = Models.FirstOrDefault(m => m.Model.Id == Selection.ActiveId); }
        finally { syncingSelection = false; }
        if (!numericEditing) RefreshInspector();
        OnPropertyChanged(nameof(WorldDimensions));
        UpdateDocument(projectPath, true); SceneChanged?.Invoke(false);
    }

    private void Commit(string name)
    {
        InvalidatePreparedImage();
        numericBefore = null;
        history.Push(name, CaptureDocument(), Selection);
        UpdateDocument(projectPath, history.IsDirty);
        OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); OnPropertyChanged(nameof(UndoDescription));
    }

    private Vector3 gesturePivotSnapshot;
    internal void BeginGesture() { CommitNumericEdit(); gestureItems = Models.ToArray(); gesturePivotSnapshot = SelectionPivot; }
    internal void UpdateGesture(Matrix4x4 delta)
    {
        if (gestureItems is null || IsBusy) return;
        try
        {
            ApplyItems(gestureItems.Select(m => Selection.Ids.Contains(m.Model.Id) && !m.Model.Locked
                ? m with { Model = m.Model with { Pose = m.Model.Pose.Transform(InstanceDelta(m, delta, gesturePivotSnapshot)) } } : m).ToArray());
        }
        catch (ArgumentException ex) { Status = ex.Message; }
    }
    internal void EndGesture(bool cancel)
    {
        if (gestureItems is not { } before) return;
        gestureItems = null;
        if (cancel) ApplyItems(before); else if (!Models.SequenceEqual(before)) Commit("Transformar selección");
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        CommitNumericEdit(); if (!history.CanUndo || IsBusy) return;
        if (BeforeHistoryRestore is { } stop) await stop();
        await RestoreAsync(history.Undo(Selection));
    }
    [RelayCommand]
    private async Task RedoAsync()
    {
        CommitNumericEdit(); if (!history.CanRedo || IsBusy) return;
        if (BeforeHistoryRestore is { } stop) await stop();
        await RestoreAsync(history.Redo(Selection));
    }
    private async Task RestoreAsync(SceneDocument document)
    {
        await RunAsync("Restaurando escena", async token =>
        {
            var prepared = new List<SceneItem>();
            foreach (var model in document.Models)
            {
                var cached = Models.FirstOrDefault(m => ReferenceEquals(m.Model.Mesh, model.Mesh));
                prepared.Add(new(model, cached?.Geometry ?? await meshAdapter.PrepareAsync(model.Mesh, token)));
            }
            sceneMetadata = document; ApplyItems(prepared);
            PreparationRestored?.Invoke(document.Preparation);
            SelectedDesign = Designs.FirstOrDefault(); OnPropertyChanged(nameof(Designs)); DesignsChanged?.Invoke();
            UpdateDocument(projectPath, history.IsDirty);
            SelectionChanged?.Invoke();
            OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); OnPropertyChanged(nameof(UndoDescription));
        });
    }

    internal Task ImportFilesAsync(IEnumerable<string> paths, float factor)
        => RunAsync("Importando lote STL", async token =>
        {
            CommitNumericEdit(); var added = new List<SceneItem>(); var errors = new List<string>();
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (Models.Count + added.Count >= ProjectStore.MaxModels) throw new InvalidDataException("Límite de modelos.");
                    var mesh = await stl.ImportAsync(path, factor, token);
                    if (Models.Concat(added).Sum(m => (long)m.Model.Mesh.TriangleCount) + mesh.TriangleCount > ProjectStore.MaxTriangles)
                        throw new InvalidDataException("Límite de triángulos.");
                    added.Add(new(new(Guid.NewGuid(), Path.GetFileNameWithoutExtension(path), mesh, ModelPose.Identity),
                        await meshAdapter.PrepareAsync(mesh, token)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
            }
            token.ThrowIfCancellationRequested();
            if (added.Count > 0)
            {
                Models = [.. Models, .. added]; SelectItems(added.Select(m => m.Model.Id)); Commit("Importar lote STL");
                SceneChanged?.Invoke(true);
            }
            Status = $"Importadas {added.Count} piezas" + (errors.Count == 0 ? "." : $" · {string.Join("; ", errors)}");
        });

    private void RemoveSelection()
    {
        if (IsBusy) return;
        CommitNumericEdit(); var ids = SelectedItems.Where(m => !m.Model.Locked).Select(m => m.Model.Id).ToHashSet();
        if (ids.Count == 0) return;
        Models = Models.Where(m => !ids.Contains(m.Model.Id)).ToArray();
        sceneMetadata = sceneMetadata with { Designs = sceneMetadata.Designs.Where(d => !ids.Contains(d.ModelId)).ToArray() };
        SelectItems([]); Commit("Quitar selección"); SceneChanged?.Invoke(false);
    }

    private void DuplicateSelection()
    {
        if (IsBusy || SelectedItems.Count == 0) return;
        CommitNumericEdit(); var originals = SelectedItems;
        if (Models.Count + originals.Count > ProjectStore.MaxModels
            || Models.Sum(m => (long)m.Model.Mesh.TriangleCount) + originals.Sum(m => (long)m.Model.Mesh.TriangleCount) > ProjectStore.MaxTriangles)
        { Status = "Límite de geometría del proyecto."; return; }
        var copies = originals.Select(m => m with { Model = m.Model with { Id = Guid.NewGuid(),
            Name = (m.Name.Length > 245 ? m.Name[..245] : m.Name) + " (copia)", GroupId = null,
            Pose = m.Model.Pose with { X = m.Model.Pose.X + 5 }, Locked = false } }).ToArray();
        var idMap = originals.Zip(copies).ToDictionary(p => p.First.Model.Id, p => p.Second.Model.Id);
        sceneMetadata = sceneMetadata with { Designs = [.. Designs, .. Designs.Where(d => idMap.ContainsKey(d.ModelId))
            .Select(d => d with { Id = Guid.NewGuid(), ModelId = idMap[d.ModelId] })] };
        OnPropertyChanged(nameof(Designs)); DesignsChanged?.Invoke();
        Models = [.. Models, .. copies]; SelectItems(copies.Select(m => m.Model.Id)); Commit("Duplicar selección"); SceneChanged?.Invoke(false);
    }

    [RelayCommand] private void SelectAll() => SelectItems(Models.Where(m => m.Model.Visible && !m.Model.Locked).Select(m => m.Model.Id));
    [RelayCommand] private void CenterSelection() => TransformSelection(Matrix4x4.CreateTranslation(-SelectionCenter.X, -SelectionCenter.Y, 0), "Centrar selección");
    [RelayCommand] private void SupportSelection() => TransformSelection(Matrix4x4.CreateTranslation(0, 0, (float)sceneMetadata.SupportZ - Bounds(SelectedItems).Min.Z), "Apoyar selección");
    [RelayCommand] private void ResetPose() => EditModels(m => m with { Pose = ModelPose.Identity }, "Restablecer pose");
    [RelayCommand] private void ToggleLock(SceneItem? item) => EditTarget(item, m => m with { Locked = !m.Locked }, "Bloquear/desbloquear");
    [RelayCommand] private void ToggleVisibility(SceneItem? item) => EditTarget(item, m => m with { Visible = !m.Visible }, "Visibilidad");
    private void EditTarget(SceneItem? target, Func<ProjectModel, ProjectModel> edit, string name)
    {
        if (target is null) { EditModels(edit, name, true); return; }
        if (IsBusy) return;
        CommitNumericEdit(); ApplyItems(Models.Select(m => m.Model.Id == target.Model.Id ? m with { Model = edit(m.Model) } : m).ToArray());
        Commit(name);
    }
    [RelayCommand] private void GroupSelection()
    {
        if (SelectedItems.Count < 2 || IsBusy) return;
        var id = Guid.NewGuid(); sceneMetadata = sceneMetadata with { Groups = [.. sceneMetadata.Groups, new(id, "Grupo")] };
        EditModels(m => m with { GroupId = id }, "Agrupar selección");
    }
    [RelayCommand] private void UngroupSelection() => EditModels(m => m with { GroupId = null }, "Desagrupar selección");
    internal Vector3 SelectionPivot => PivotIndex switch
    {
        1 when Selected is { } active => new((float)active.Model.Pose.X, (float)active.Model.Pose.Y, (float)active.Model.Pose.Z),
        2 => Vector3.Zero,
        _ => SelectionCenter
    };
    private Matrix4x4 InstanceDelta(SceneItem item, Matrix4x4 delta, Vector3 pivot)
    {
        if (PivotIndex != 3) return delta;
        var origin = new Vector3((float)item.Model.Pose.X, (float)item.Model.Pose.Y, (float)item.Model.Pose.Z);
        return Matrix4x4.CreateTranslation(-origin) * Matrix4x4.CreateTranslation(pivot) * delta
            * Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateTranslation(origin);
    }
    [RelayCommand] private void RenameSelection()
    {
        if (string.IsNullOrWhiteSpace(RenameText) || RenameText.Length > 245) { Status = "Nombre de 1 a 245 caracteres."; return; }
        var n = 0; EditModels(m => m with { Name = Selection.Ids.Count == 1 ? RenameText.Trim() : $"{RenameText.Trim()} {++n}" }, "Renombrar selección");
    }
    [RelayCommand] private void AlignX() => AlignAxis(0);
    [RelayCommand] private void AlignY() => AlignAxis(1);
    private void AlignAxis(int axis)
    {
        var target = SelectionCenter;
        EditModels(m =>
        {
            var bounds = Bounds(Models.Where(item => item.Model.Id == m.Id)); var center = (bounds.Min + bounds.Max) / 2;
            return m with { Pose = m.Pose.Transform(Matrix4x4.CreateTranslation(axis == 0 ? target.X - center.X : 0,
                axis == 1 ? target.Y - center.Y : 0, 0)) };
        }, axis == 0 ? "Alinear X" : "Alinear Y");
    }
    [RelayCommand] private void DistributeX()
    {
        var ordered = SelectedItems.OrderBy(m => Bounds([m]).Min.X).ToArray(); if (ordered.Length < 2) return;
        var cursor = Bounds([ordered[0]]).Min.X; var changes = new Dictionary<Guid, ModelPose>();
        foreach (var item in ordered)
        {
            var bounds = Bounds([item]); changes[item.Model.Id] = item.Model.Pose.Transform(Matrix4x4.CreateTranslation(cursor - bounds.Min.X, 0, 0));
            cursor += bounds.Max.X - bounds.Min.X + 5;
        }
        EditModels(m => m with { Pose = changes.GetValueOrDefault(m.Id, m.Pose) }, "Distribuir X con separación 5 mm");
    }
    partial void OnSupportHeightChanged(string value)
    {
        if (!Number(value, out var height)) { Status = "Altura de apoyo inválida."; return; }
        if (height == sceneMetadata.SupportZ) return;
        sceneMetadata = sceneMetadata with { SupportZ = height }; Commit("Cambiar plano de apoyo"); SceneChanged?.Invoke(false);
    }
    internal Vector3 SelectionCenter => (Bounds(SelectedItems).Min + Bounds(SelectedItems).Max) * 0.5f;
    internal void TransformSelection(Matrix4x4 delta, string name) { BeginGesture(); UpdateGesture(delta); gestureItems = null; Commit(name); }
    private void EditModels(Func<ProjectModel, ProjectModel> edit, string name, bool locked = false)
    {
        if (IsBusy || SelectedItems.Count == 0) return;
        CommitNumericEdit(); ApplyItems(Models.Select(m => Selection.Ids.Contains(m.Model.Id) && (locked || !m.Model.Locked)
            ? m with { Model = edit(m.Model) } : m).ToArray()); Commit(name);
    }
    internal static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<SceneItem> items)
    {
        var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
        foreach (var item in items)
        {
            var lo = item.Model.Mesh.Minimum; var hi = item.Model.Mesh.Maximum;
            for (var i = 0; i < 8; i++)
            {
                var p = Vector3.Transform(new((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y,
                    (i & 4) == 0 ? lo.Z : hi.Z), item.Model.Pose.Matrix);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
        }
        return float.IsFinite(min.X) ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }
    private static string Format(Vector3 v) => $"{v.X:0.###} × {v.Y:0.###} × {v.Z:0.###} mm";
}
