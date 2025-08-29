namespace Ralen;

public class RuntimeRegistry
{
    public readonly string root;
    public RuntimeRegistry(string rootPath) => root = rootPath;
    public string GetPath(string lang, string version)
        => Path.Combine(root, "versions", lang, version, "bin", LanguageCatalog.Catalog[lang].RuntimeExecutables[0]);

    public IEnumerable<string> ListInstalledVersions(string lang)
    {
        var dir = Path.Combine(root, "versions", lang);
        if (!Directory.Exists(dir)) yield break;
        foreach (var d in Directory.EnumerateDirectories(dir))
            yield return Path.GetFileName(d);
    }
    public bool IsInstalled(string lang, string version) => File.Exists(GetPath(lang, version));
    public string Root => root;
}
