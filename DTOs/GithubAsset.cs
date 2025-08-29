using System.Text.Json.Serialization;

namespace Ralen;

public record GithubAsset
{
    [JsonPropertyName("name")]                 public string Name { get; init; }
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; init; }
    [JsonPropertyName("content_type")]         public string ContentType { get; init; }
}
