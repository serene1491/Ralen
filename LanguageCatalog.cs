namespace Ralen;

public static class LanguageCatalog
{
    public static readonly Dictionary<string, LanguageDefinition> Catalog =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["salang"] = new LanguageDefinition("salang", new[] { "salang", "salang.exe", "SaLang" })
            {
                RepoOwner = "serene1491",
                RepoName = "SaLang",
                AssetNameRegex = @"^SaLang(|\.exe|\.zip|\.tar\.gz)$",
                FallbackAssetName = "SaLang"
            },
        };
}
