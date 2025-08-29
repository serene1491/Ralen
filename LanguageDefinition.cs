namespace Ralen;

public record LanguageDefinition
{
    public string Key { get; init; }
    public string[] RuntimeExecutables { get; init; }
    public string[] KnownBadVersions { get; init; } = Array.Empty<string>();
    public string VersionArg { get; init; } = "--version";
    public string RepoOwner { get; init; } = string.Empty;
    public string RepoName { get; init; } = string.Empty;
    public string AssetNameRegex { get; init; }
    public string FallbackAssetName { get; init; }

    public LanguageDefinition(string key, string[] runtimeExecutables)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        RuntimeExecutables = runtimeExecutables ?? throw new ArgumentNullException(nameof(runtimeExecutables));
    }
}
