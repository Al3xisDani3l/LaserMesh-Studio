using System.IO.Compression;
using System.Text.Json;
using LaserMesh.Geometry;

namespace LaserMesh.Projects;

/// <summary>Reads and atomically replaces versioned LaserMesh projects.</summary>
public sealed class ProjectStore(StlCodec stl)
{
    /// <summary>Maximum number of embedded models.</summary>
    public const int MaxModels = 128;

    /// <summary>Maximum aggregate triangle count.</summary>
    public const int MaxTriangles = 2_000_000;

    private const int Version = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
    private const string ManifestName = "manifest.json";
    private const int MaxManifestBytes = 2 * 1024 * 1024;

    /// <summary>Loads embedded models and placements on a background thread.</summary>
    public Task<IReadOnlyList<ProjectModel>> OpenAsync(string path, CancellationToken cancellationToken)
        => Task.Run(() => Read(path, cancellationToken).Models, cancellationToken);

    /// <summary>Saves a snapshot using a sibling temporary file and atomic rename.</summary>
    public Task SaveAsync(string path, IReadOnlyList<ProjectModel> models, CancellationToken cancellationToken)
    {
        var snapshot = models.ToArray();
        return SaveSceneAsync(path, new SceneDocument { Models = snapshot }, cancellationToken);
    }

    /// <summary>Loads a version-one or version-two scene with embedded images.</summary>
    public Task<SceneDocument> OpenSceneAsync(string path, CancellationToken cancellationToken)
        => Task.Run(() => Read(path, cancellationToken), cancellationToken);

    /// <summary>Saves an entire version-two scene atomically.</summary>
    public Task SaveSceneAsync(string path, SceneDocument document, CancellationToken cancellationToken)
        => Task.Run(() => Write(path, document, cancellationToken), cancellationToken);

    private SceneDocument Read(string path, CancellationToken token)
    {
        using var input = File.OpenRead(path);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > MaxModels + 129)
            throw new InvalidDataException("Demasiados recursos en el proyecto.");

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            if (!entries.TryAdd(entry.FullName, entry))
                throw new InvalidDataException("El proyecto contiene recursos duplicados.");
        }

        if (!entries.TryGetValue(ManifestName, out var manifestEntry) || manifestEntry.Length > MaxManifestBytes)
            throw new InvalidDataException("Manifest ausente o demasiado grande.");

        using var manifestStream = manifestEntry.Open();
        var json = new byte[checked((int)manifestEntry.Length)];
        manifestStream.ReadExactly(json);
        if (manifestStream.ReadByte() != -1)
            throw new InvalidDataException("La longitud del manifest no coincide.");

        var manifest = JsonSerializer.Deserialize<Manifest>(json, JsonOptions);
        if (manifest is null || manifest.Version is not (1 or Version) || manifest.Unit != "mm"
            || manifest.Models is null || manifest.Models.Length > MaxModels)
            throw new InvalidDataException("Formato de proyecto o versión no admitidos.");

        var result = new List<ProjectModel>(manifest.Models.Length);
        var ids = new HashSet<Guid>();
        var triangles = 0;
        foreach (var item in manifest.Models)
        {
            token.ThrowIfCancellationRequested();
            if (item is null || !IsValid(item) || !ids.Add(item.Id)
                || !entries.TryGetValue(MeshPath(item.Id), out var entry)
                || entry.Length < 134 || entry.Length > 84 + 50L * TriangleMesh.MaxTriangles
                || (entry.Length - 84) % 50 != 0)
                throw new InvalidDataException("Referencia de malla inválida en el proyecto.");

            var expectedTriangles = (int)((entry.Length - 84) / 50);
            triangles += expectedTriangles;
            if (triangles > MaxTriangles)
                throw new InvalidDataException("El proyecto excede el límite de triángulos.");

            using var stream = entry.Open();
            var mesh = stl.Read(stream, entry.Length, 1, token);
            if (mesh.TriangleCount != expectedTriangles || stream.ReadByte() != -1)
                throw new InvalidDataException("Longitud de malla inconsistente.");
            result.Add(new(item.Id, item.Name, mesh, item.Pose) { Visible = item.Visible, Locked = item.Locked, GroupId = item.GroupId });
        }

        var assets = new List<DesignAsset>();
        long resourceBytes = 0;
        foreach (var asset in manifest.Assets ?? [])
        {
            token.ThrowIfCancellationRequested();
            if (!entries.TryGetValue($"images/{asset.Id:N}", out var entry) || entry.Length is <= 0 or > 64 * 1024 * 1024)
                throw new InvalidDataException("Imagen embebida inválida.");
            resourceBytes += entry.Length;
            if (resourceBytes > 128 * 1024 * 1024) throw new InvalidDataException("Demasiados recursos de imagen.");
            using var stream = entry.Open();
            var content = new byte[checked((int)entry.Length)]; stream.ReadExactly(content);
            if (stream.ReadByte() != -1) throw new InvalidDataException("Longitud de imagen inconsistente.");
            assets.Add(new(asset.Id, asset.Name, asset.MediaType, content));
        }
        var document = new SceneDocument { Models = result, Assets = assets, Groups = manifest.Groups ?? [],
            Designs = manifest.Designs ?? [], SupportZ = manifest.SupportZ, SectionZ = manifest.SectionZ,
            Preparation = manifest.Preparation ?? new() };
        ValidateScene(document);
        return document;
    }

    private void Write(string path, SceneDocument document, CancellationToken token)
    {
        ValidateScene(document);
        var models = document.Models.ToArray();
        var items = models.Select(x => new ModelEntry(x.Id, x.Name, x.Pose, x.Visible, x.Locked, x.GroupId)).ToArray();
        if (items.Length > MaxModels || items.Any(x => !IsValid(x))
            || items.Select(x => x.Id).Distinct().Count() != items.Length
            || models.Sum(x => (long)x.Mesh.TriangleCount) > MaxTriangles)
            throw new InvalidDataException("El proyecto contiene modelos inválidos o excede sus límites.");

        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".lmesh-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var manifest = JsonSerializer.SerializeToUtf8Bytes(new Manifest(Version, "mm", items, document.Groups.ToArray(),
                        document.Assets.Select(a => new AssetEntry(a.Id, a.Name, a.MediaType)).ToArray(),
                        document.Designs.ToArray(), document.SupportZ, document.SectionZ, document.Preparation), JsonOptions);
                    if (manifest.Length > MaxManifestBytes) throw new InvalidDataException("Manifest demasiado grande.");
                    using (var stream = zip.CreateEntry(ManifestName).Open()) stream.Write(manifest);
                    foreach (var asset in document.Assets)
                    {
                        token.ThrowIfCancellationRequested();
                        using var image = zip.CreateEntry($"images/{asset.Id:N}", CompressionLevel.Fastest).Open();
                        image.Write(asset.Content);
                    }
                    foreach (var model in models)
                    {
                        token.ThrowIfCancellationRequested();
                        using var mesh = zip.CreateEntry(MeshPath(model.Id), CompressionLevel.Fastest).Open();
                        stl.WriteBinary(mesh, model.Mesh, token);
                    }
                }
                output.Flush(flushToDisk: true);
            }

            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static bool IsValid(ModelEntry item) => item.Id != Guid.Empty
        && item.Name is { Length: <= 255 } && !string.IsNullOrWhiteSpace(item.Name) && item.Pose.IsValid();

    private static string MeshPath(Guid id) => $"meshes/{id:N}.stl";

    private static void ValidateScene(SceneDocument document)
    {
        var models = document.Models.Select(m => m.Id).ToHashSet();
        var assets = document.Assets.Select(a => a.Id).ToHashSet();
        var groups = document.Groups.Select(g => g.Id).ToHashSet();
        if (!double.IsFinite(document.SupportZ) || !double.IsFinite(document.SectionZ)
            || !double.IsFinite(document.Preparation.Height)
            || !float.IsFinite(document.Preparation.FacePoint.LengthSquared())
            || !float.IsFinite(document.Preparation.FaceNormal.LengthSquared()) || document.Preparation.FaceNormal.LengthSquared() < 0.0001
            || document.Preparation.Mode is not ("Silhouette" or "Rectangle" or "Individual" or "Section")
            || document.Preparation.Reference is not ("Superior" or "Inferior" or "Support" or "Custom" or "Face")
            || document.Assets.Count > 128 || assets.Count != document.Assets.Count
            || groups.Count != document.Groups.Count || document.Groups.Count > MaxModels
            || document.Groups.Any(g => g.Id == Guid.Empty || string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 255)
            || document.Models.Any(m => m.GroupId is { } g && !groups.Contains(g))
            || document.Assets.Any(a => a.Id == Guid.Empty || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 255
                || a.MediaType is not ("image/png" or "image/jpeg" or "application/x-lasermesh-uv") || a.Content.Length is <= 0 or > 64 * 1024 * 1024)
            || document.Assets.Sum(a => (long)a.Content.Length) > 128 * 1024 * 1024
            || document.Designs.Count > 512 || document.Designs.Select(d => d.Id).Distinct().Count() != document.Designs.Count
            || document.Designs.Any(d => d.Id == Guid.Empty || !assets.Contains(d.AssetId) || !models.Contains(d.ModelId)
                || string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 255
                || d.AtlasId is { } atlasId && !assets.Contains(atlasId)
                || !double.IsFinite(d.CylinderRadius) || d.CylinderRadius <= 0 || !double.IsFinite(d.SeamDegrees)
                || !float.IsFinite(d.Crop.LengthSquared()) || d.Crop.X < 0 || d.Crop.Y < 0 || d.Crop.Z > 1 || d.Crop.W > 1
                || d.Crop.Z <= d.Crop.X || d.Crop.W <= d.Crop.Y || d.Islands.Count > 100000
                || d.Islands.Any(i => i.Chart < 0 || !double.IsFinite(i.Scale) || i.Scale <= 0 || !double.IsFinite(i.Angle) || !float.IsFinite(i.Offset.LengthSquared()))
                || !Enum.IsDefined(d.Projection) || !double.IsFinite(d.Width) || !double.IsFinite(d.Height)
                || d.Width <= 0 || d.Height <= 0 || !double.IsFinite(d.Angle) || !(d.Opacity is >= 0 and <= 1)
                || !float.IsFinite(d.Origin.LengthSquared()) || !float.IsFinite(d.Normal.LengthSquared())
                || d.Normal.LengthSquared() < 0.0001f))
            throw new InvalidDataException("Recursos, grupos o diseños de escena inválidos.");
    }

    private sealed record Manifest(int Version, string Unit, ModelEntry[] Models, SceneGroup[]? Groups = null,
        AssetEntry[]? Assets = null, SurfaceDesign[]? Designs = null, double SupportZ = 0, double SectionZ = 0,
        PreparationSettings? Preparation = null);
    private sealed record AssetEntry(Guid Id, string Name, string MediaType);
    private sealed record ModelEntry(Guid Id, string Name, ModelPose Pose, bool Visible = true, bool Locked = false, Guid? GroupId = null);
}
