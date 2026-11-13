using System.Numerics;
using System.Collections.Frozen;

namespace LaserMesh.Projects;

/// <summary>Image resource embedded in a project.</summary>
/// <param name="Id">Stable resource identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="MediaType">PNG or JPEG media type.</param>
/// <param name="Content">Encoded image bytes, treated as immutable.</param>
public sealed record DesignAsset(Guid Id, string Name, string MediaType, byte[] Content);

/// <summary>Supported image-to-surface mappings.</summary>
public enum DesignProjection
{
    /// <summary>Projection onto a tangent plane.</summary>
    Planar,
    /// <summary>Angular cylinder mapping.</summary>
    Cylindrical,
    /// <summary>Generated mesh atlas mapping.</summary>
    Uv
}

/// <summary>Image placement in the target model's local coordinates.</summary>
/// <param name="Id">Design instance identifier.</param>
/// <param name="AssetId">Embedded image identifier.</param>
/// <param name="ModelId">Target model identifier.</param>
/// <param name="Origin">Local mapping origin.</param>
/// <param name="Normal">Local plane normal or cylinder axis.</param>
/// <param name="Width">Physical width in millimetres.</param>
/// <param name="Height">Physical height in millimetres.</param>
/// <param name="Angle">Rotation in degrees.</param>
/// <param name="Opacity">Image alpha multiplier.</param>
/// <param name="Projection">Mapping method.</param>
public sealed record SurfaceDesign(Guid Id, Guid AssetId, Guid ModelId, Vector3 Origin, Vector3 Normal,
    double Width, double Height, double Angle, double Opacity, DesignProjection Projection)
{
    /// <summary>Human-readable design label.</summary>
    public string Name { get; init; } = "Diseño";
    /// <summary>Generated atlas binary resource, when using UV mapping.</summary>
    public Guid? AtlasId { get; init; }
    /// <summary>Cylinder radius in local millimetres.</summary>
    public double CylinderRadius { get; init; } = 10;
    /// <summary>Angular seam position in degrees.</summary>
    public double SeamDegrees { get; init; }
    /// <summary>Normalized image crop rectangle, left/top/right/bottom.</summary>
    public Vector4 Crop { get; init; } = new(0, 0, 1, 1);
    /// <summary>Per-island nondestructive edits.</summary>
    public IReadOnlyList<UvIslandPlacement> Islands { get; init; } = [];
}

/// <summary>One atlas island transform about its own bounds center.</summary>
/// <param name="Chart">Original chart identity.</param>
/// <param name="Offset">Normalized atlas displacement.</param>
/// <param name="Scale">Uniform positive scale.</param>
/// <param name="Angle">Rotation in degrees.</param>
public sealed record UvIslandPlacement(int Chart, Vector2 Offset, double Scale, double Angle);

/// <summary>Named group of scene instances.</summary>
/// <param name="Id">Group identity.</param>
/// <param name="Name">Display name.</param>
public sealed record SceneGroup(Guid Id, string Name);

/// <summary>Persisted guide configuration without hardware/session state.</summary>
/// <param name="Mode">Silhouette, Rectangle, Individual or Section.</param>
/// <param name="Reference">Superior, Inferior, Support, Custom or Face.</param>
/// <param name="AllVisible">Whether scope includes all visible models.</param>
/// <param name="Height">Explicit section/custom reference height.</param>
public sealed record PreparationSettings(string Mode = "Silhouette", string Reference = "Superior", bool AllVisible = false, double Height = 0)
{
    /// <summary>Optional picked-face instance identity.</summary>
    public Guid? FaceModel { get; init; }
    /// <summary>Picked point in model-local coordinates.</summary>
    public Vector3 FacePoint { get; init; }
    /// <summary>Picked normal in model-local coordinates.</summary>
    public Vector3 FaceNormal { get; init; } = Vector3.UnitZ;
}

/// <summary>Serializable project snapshot without graphics or connected devices.</summary>
public sealed record SceneDocument
{
    /// <summary>Immutable model instances.</summary>
    public IReadOnlyList<ProjectModel> Models { get; init; } = [];
    /// <summary>Editing groups.</summary>
    public IReadOnlyList<SceneGroup> Groups { get; init; } = [];
    /// <summary>Encoded image resources.</summary>
    public IReadOnlyList<DesignAsset> Assets { get; init; } = [];
    /// <summary>Design placements.</summary>
    public IReadOnlyList<SurfaceDesign> Designs { get; init; } = [];
    /// <summary>Support plane height in scene millimetres.</summary>
    public double SupportZ { get; init; }
    /// <summary>Independent horizontal section height.</summary>
    public double SectionZ { get; init; }
    /// <summary>Saved guide scope, mode and reference.</summary>
    public PreparationSettings Preparation { get; init; } = new();
}

/// <summary>Stable selected identities with one active instance.</summary>
public sealed class SelectionState
{
    private readonly HashSet<Guid> selected = [];
    private IReadOnlySet<Guid> snapshot = FrozenSet<Guid>.Empty;
    /// <summary>Read-only view of selected identities.</summary>
    public IReadOnlySet<Guid> Ids => snapshot;
    /// <summary>Identity used as the active editing reference.</summary>
    public Guid? ActiveId { get; private set; }
    /// <summary>Replaces the selection with distinct nonempty identities.</summary>
    public void Replace(IEnumerable<Guid> ids, Guid? active = null)
    {
        selected.Clear();
        foreach (var id in ids) if (id != Guid.Empty) selected.Add(id);
        ActiveId = active is { } value && selected.Contains(value) ? value : selected.Cast<Guid?>().LastOrDefault();
        snapshot = selected.ToFrozenSet();
    }
    /// <summary>Toggles one identity without replacing other selected instances.</summary>
    public void Toggle(Guid id)
    {
        if (id == Guid.Empty) return;
        if (selected.Remove(id)) ActiveId = selected.Cast<Guid?>().LastOrDefault();
        else { selected.Add(id); ActiveId = id; }
        snapshot = selected.ToFrozenSet();
    }
}

/// <summary>Snapshot history with stable save checkpoint and bounded retained transactions.</summary>
public sealed class EditHistory
{
    private sealed record Entry(long Revision, string Name, SceneDocument Document, Guid[] Selection, Guid? Active);
    private readonly List<Entry> entries = [];
    private int position = -1;
    private long sequence;
    private long saved;
    /// <summary>Whether an earlier snapshot exists.</summary>
    public bool CanUndo => position > 0;
    /// <summary>Whether a later snapshot exists.</summary>
    public bool CanRedo => position >= 0 && position < entries.Count - 1;
    /// <summary>Whether the current revision differs from the save checkpoint.</summary>
    public bool IsDirty => position >= 0 && entries[position].Revision != saved;
    /// <summary>Label of the operation that can be undone.</summary>
    public string UndoName => CanUndo ? entries[position].Name : "";
    /// <summary>Starts history for a newly opened or empty project.</summary>
    public void Reset(SceneDocument document, SelectionState selection)
    {
        entries.Clear(); position = -1;
        Push("", document, selection); MarkSaved();
    }
    /// <summary>Commits a completed editing transaction; discards the redo branch.</summary>
    public void Push(string name, SceneDocument document, SelectionState selection)
    {
        if (position + 1 < entries.Count) entries.RemoveRange(position + 1, entries.Count - position - 1);
        entries.Add(new(++sequence, name, document, selection.Ids.ToArray(), selection.ActiveId));
        if (entries.Count > 128) entries.RemoveAt(0);
        position = entries.Count - 1;
    }
    /// <summary>Marks the current revision as atomically saved.</summary>
    public void MarkSaved() { if (position >= 0) saved = entries[position].Revision; }
    /// <summary>Restores one transaction backwards and its selection.</summary>
    public SceneDocument Undo(SelectionState selection) => Restore(CanUndo ? position - 1 : position, selection);
    /// <summary>Restores one transaction forwards and its selection.</summary>
    public SceneDocument Redo(SelectionState selection) => Restore(CanRedo ? position + 1 : position, selection);
    private SceneDocument Restore(int at, SelectionState selection)
    {
        if (at < 0) throw new InvalidOperationException("Historial sin inicializar.");
        position = at; var entry = entries[at]; selection.Replace(entry.Selection, entry.Active); return entry.Document;
    }
}
