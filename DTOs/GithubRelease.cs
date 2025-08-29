using System.Text.Json.Serialization;

namespace Ralen;

public record GithubRelease
{
    [JsonPropertyName("tag_name")]    public string TagName { get; init; }
    [JsonPropertyName("name")]        public string Name { get; init; }
    [JsonPropertyName("assets")]      public GithubAsset[] Assets { get; init; } = Array.Empty<GithubAsset>();
    [JsonPropertyName("zipball_url")] public string ZipballUrl { get; init; }
    [JsonPropertyName("tarball_url")] public string TarballUrl { get; init; }
}