using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Ralen;

public class RemoteResolver
{
    readonly HttpClient http;

    public RemoteResolver(HttpClient httpClient)
    {
        http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        // Ensure caller set UserAgent
    }

    /// <summary>Fetches a release (by tag or latest). If not found it will attempt to fall back to repository info (default branch) and return a pseudo-release pointing to that zipball.</summary>
    public async Task<GithubRelease> GetReleaseInfoAsync(string owner, string repo, string tagOrNull)
    {
        string api = string.IsNullOrEmpty(tagOrNull)
            ? $"https://api.github.com/repos/{owner}/{repo}/releases/latest"
            : $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tagOrNull)}";

        using var resp = await http.GetAsync(api);
        if (resp.IsSuccessStatusCode)
        {
            var json = await resp.Content.ReadAsStringAsync();
            try
            {
                var rel = JsonSerializer.Deserialize<GithubRelease>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return rel;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to parse GitHub release JSON: " + ex.Message);
                return null;
            }
        }
        else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // fallback: query repo to obtain default_branch and return a pseudo-release with ZipballUrl
            var repoApi = $"https://api.github.com/repos/{owner}/{repo}";
            using var repoResp = await http.GetAsync(repoApi);
            if (!repoResp.IsSuccessStatusCode)
            {
                Console.WriteLine($"Repository API request failed: {repoResp.StatusCode} {repoResp.ReasonPhrase}");
                return null;
            }

            var repoJson = await repoResp.Content.ReadAsStringAsync();
            GithubRepo repoInfo = null;
            try
            {
                repoInfo = JsonSerializer.Deserialize<GithubRepo>(repoJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to parse repository JSON: " + ex.Message);
                return null;
            }

            if (repoInfo == null || string.IsNullOrEmpty(repoInfo.DefaultBranch))
            {
                Console.WriteLine("Could not determine repository default branch for fallback.");
                return null;
            }

            var fallback = new GithubRelease
            {
                TagName = repoInfo.DefaultBranch,
                Name = $"default-branch-{repoInfo.DefaultBranch}",
                ZipballUrl = $"https://api.github.com/repos/{owner}/{repo}/zipball/{Uri.EscapeDataString(repoInfo.DefaultBranch)}",
                Assets = Array.Empty<GithubAsset>()
            };

            Console.WriteLine($"Falling back to the default branch '{repoInfo.DefaultBranch}' zipball: {fallback.ZipballUrl}");
            return fallback;
        }
        else
        {
            Console.WriteLine($"GitHub API request failed: {resp.StatusCode} {resp.ReasonPhrase}");
            return null;
        }
    }

    /// <summary>
    /// Choose a downloadable URL for the release: preferentially asset matching regex or runtime names;
    /// fallback to zipball/tarball; finally try releases/download/{tag}/{name} candidates but test them with HEAD.
    /// Returns null if nothing suitable found.
    /// </summary>
    public async Task<string> SelectDownloadUrlAsync(GithubRelease release, LanguageDefinition def, string owner, string repo)
    {
        // 1) AssetNameRegex
        if (!string.IsNullOrWhiteSpace(def.AssetNameRegex) && release.Assets?.Length > 0)
        {
            var rx = new Regex(def.AssetNameRegex, RegexOptions.IgnoreCase);
            var asset = release.Assets.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Name) && rx.IsMatch(a.Name));
            if (asset != null && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                return asset.BrowserDownloadUrl;
        }

        // 2) Match by runtime executable filenames
        if (release.Assets?.Length > 0)
        {
            var names = def.RuntimeExecutables.Select(n => n.ToLowerInvariant()).ToHashSet();
            var assetByExe = release.Assets.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.Name) &&
                names.Contains(Path.GetFileName(a.Name).ToLowerInvariant()));
            if (assetByExe != null && !string.IsNullOrWhiteSpace(assetByExe.BrowserDownloadUrl))
                return assetByExe.BrowserDownloadUrl;
        }

        // 3) Any asset fallback
        if (release.Assets?.Length > 0 && !string.IsNullOrWhiteSpace(release.Assets[0].BrowserDownloadUrl))
            return release.Assets[0].BrowserDownloadUrl;

        // 4) zipball/tarball provided by release
        if (!string.IsNullOrWhiteSpace(release.ZipballUrl)) return release.ZipballUrl;
        if (!string.IsNullOrWhiteSpace(release.TarballUrl)) return release.TarballUrl;

        // 5) If we have a tag, attempt releases/download/{tag}/{name} candidates (test with HEAD)
        if (!string.IsNullOrWhiteSpace(release.TagName))
        {
            var tagEsc = Uri.EscapeDataString(release.TagName);
            var baseName = !string.IsNullOrWhiteSpace(def.FallbackAssetName) ? def.FallbackAssetName : def.Key;
            var candidates = new List<string>();

            // order: prefer .exe on Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{tagEsc}/{baseName}.exe");
                candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{tagEsc}/{baseName}");
                candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{tagEsc}/{baseName}.zip");
            }
            else
            {
                candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{tagEsc}/{baseName}");
                candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{tagEsc}/{baseName}.exe");
                candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{tagEsc}/{baseName}.zip");
            }

            foreach (var c in candidates)
            {
                if (await HeadExistsAsync(c))
                    return c;
            }
        }

        // nothing
        Console.WriteLine("No tag found for release; and no assets/zipball available to download (or candidates 404).");
        return null;
    }

    async Task<bool> HeadExistsAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var res = await http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Get default branch name or null</summary>
    public async Task<string> GetRepoDefaultBranchAsync(string owner, string repo)
    {
        try
        {
            var repoApi = $"https://api.github.com/repos/{owner}/{repo}";
            using var resp = await http.GetAsync(repoApi);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var info = JsonSerializer.Deserialize<GithubRepo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return info?.DefaultBranch;
        }
        catch { return null; }
    }
}
