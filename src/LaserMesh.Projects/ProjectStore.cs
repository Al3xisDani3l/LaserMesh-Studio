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

    private const int Version = 1;
    private const string ManifestName = "manifest.json";
    private const int MaxManifestBytes = 256 * 1024;

    /// <summary>Loads embedded models and placements on a background thread.</summary>
    public Task<IReadOnlyList<ProjectModel>> OpenAsync(string path, CancellationToken cancellationToken)
        => Task.Run(() => Read(path, cancellationToken), cancellationToken);

    /// <summary>Saves a snapshot using a sibling temporary file and atomic rename.</summary>
    public Task SaveAsync(string path, IReadOnlyList<ProjectModel> models, CancellationToken cancellationToken)
    {
        var snapshot = models.ToArray();
        return Task.Run(() => Write(path, snapshot, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<ProjectModel> Read(string path, CancellationToken token)
    {
        using var input = File.OpenRead(path);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > MaxModels + 1)
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

        var manifest = JsonSerializer.Deserialize<Manifest>(json);
        if (manifest is null || manifest.Version != Version || manifest.Unit != "mm"
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
            result.Add(new(item.Id, item.Name, mesh, item.Pose));
        }

        return result;
    }

    private void Write(string path, ProjectModel[] models, CancellationToken token)
    {
        var items = models.Select(x => new ModelEntry(x.Id, x.Name, x.Pose)).ToArray();
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
                    using (var stream = zip.CreateEntry(ManifestName).Open())
                        JsonSerializer.Serialize(stream, new Manifest(Version, "mm", items));
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

    private sealed record Manifest(int Version, string Unit, ModelEntry[] Models);
    private sealed record ModelEntry(Guid Id, string Name, ModelPose Pose);
}
