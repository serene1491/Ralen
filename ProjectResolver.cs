using System.Text.Json;
namespace Ralen;

public static class ProjectResolver
{
    public static ProjectDescriptor FindSingleProjectInDirectory(string dir)
    {
        var found = FindProjectsInDirectory(dir).ToArray();
        if (found.Length == 0) return null;
        if (found.Length == 1) return found[0];
        // ambiguous - caller should handle multiple
        return null;
    }

    public static IEnumerable<ProjectDescriptor> FindProjectsInDirectory(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.ralenproj", SearchOption.TopDirectoryOnly))
        {
            var json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var name = root.GetProperty("name").GetString() ?? Path.GetFileNameWithoutExtension(file);
            var lang = root.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";
            var ver = root.TryGetProperty("version", out var v) ? v.GetString() ?? "latest" : "latest";
            var entry = root.TryGetProperty("entry", out var e) ? e.GetString() ?? "" : "";
            yield return new ProjectDescriptor(file, name, lang, ver, entry);
        }
    }
}
