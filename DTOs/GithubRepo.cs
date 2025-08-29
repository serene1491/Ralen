using System.Text.Json.Serialization;
namespace Ralen;

public record GithubRepo
{
    [JsonPropertyName("default_branch")] public string DefaultBranch { get; init; }
}
