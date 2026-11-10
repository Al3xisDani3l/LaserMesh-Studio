using System.IO;
using System.Text.Json;

namespace LaserMesh.Studio;

internal sealed class RecentProjects(string path)
{
    public string[] Load()
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024)
            return [];
        return (JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(path)) ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x) && Path.IsPathFullyQualified(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    }

    public void Save(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(paths));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
