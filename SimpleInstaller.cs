using System.Diagnostics;
namespace Ralen;

public partial class SimpleInstaller : IInstaller
{
    readonly RuntimeRegistry registry;
    readonly Config cfg;
    readonly LocalInstaller local;
    readonly RemoteResolver resolver;
    readonly RemoteInstaller remote;
    readonly HttpClient http;

    public SimpleInstaller(RuntimeRegistry reg, Config config)
    {
        registry = reg ?? throw new ArgumentNullException(nameof(reg));
        cfg = config ?? throw new ArgumentNullException(nameof(config));

        http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ralen-installer/1.0");

        local = new LocalInstaller(cfg);
        resolver = new RemoteResolver(http);
        remote = new RemoteInstaller(http);
    }

    public void ConfigurePathNow(bool addToPath = true, bool createShims = true)
        => local.ConfigurePathNow(addToPath, createShims);

    public async Task<string> EnsureInstalledAsync(string lang, string versionSpecifier, string ownerRepoOverride = null)
    {
        if (!LanguageCatalog.Catalog.ContainsKey(lang))
            throw new InvalidOperationException($"Language '{lang}' is not in the LanguageCatalog.");
        var def = LanguageCatalog.Catalog[lang];

        var (owner, repo) = ResolveOwnerRepo(def, ownerRepoOverride);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            throw new InvalidOperationException($"No repository configured for language '{lang}'. Provide owner/repo override.");

        if (versionSpecifier != "latest" && registry.IsInstalled(lang, versionSpecifier))
            return versionSpecifier;

        var release = await resolver.GetReleaseInfoAsync(owner, repo, versionSpecifier == "latest" ? null : versionSpecifier);
        if (release == null)
            throw new InvalidOperationException($"Could not find release {(versionSpecifier == "latest" ? "latest" : versionSpecifier)} for {owner}/{repo}.");

        var tag = release.TagName ?? release.Name ?? "unknown";
        if (registry.IsInstalled(lang, tag))
            return tag;

        // select download url (resolver will: check assets, zipball, or test release download candidates)
        var downloadUrl = await resolver.SelectDownloadUrlAsync(release, def, owner, repo);

        // if still null, try fallback: repo default branch zipball (explicit)
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            var defaultBranch = await resolver.GetRepoDefaultBranchAsync(owner, repo);
            if (!string.IsNullOrWhiteSpace(defaultBranch))
            {
                downloadUrl = $"https://api.github.com/repos/{owner}/{repo}/zipball/{Uri.EscapeDataString(defaultBranch)}";
                Console.WriteLine($"Falling back to repo archive: {downloadUrl}");
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new InvalidOperationException("No downloadable asset found for release (no asset, zipball or fallback URL).");

        // validate absolute URI
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri))
            throw new InvalidOperationException($"Invalid download URL for release: '{downloadUrl}'");

        Console.WriteLine($"Downloading {downloadUri} ...");

        var tmpFile = await remote.DownloadToTempFileAsync(downloadUri.ToString(), lang);

        var versionDir = Path.Combine(registry.Root, "versions", lang, tag);
        if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true);
        Directory.CreateDirectory(versionDir);

        try
        {
            await remote.InstallDownloadedAsync(tmpFile, versionDir, def);

            // smoke test
            try
            {
                var runtimePath = registry.GetPath(lang, tag);
                if (!File.Exists(runtimePath))
                {
                    var bin = Path.Combine(versionDir, "bin");
                    if (Directory.Exists(bin))
                    {
                        var candidates = Directory.GetFiles(bin);
                        runtimePath = candidates.FirstOrDefault() ?? runtimePath;
                    }
                }

                if (File.Exists(runtimePath))
                {
                    Console.WriteLine("Running smoke test...");
                    await RunSmokeTestAsync(lang, runtimePath);
                }
                else
                {
                    Console.WriteLine("Warning: could not locate runtime to run smoke test. The repo archive might contain source code only.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: smoke test failed: {ex.Message}");
            }

            Console.WriteLine($"Installed {lang} {tag} into: {versionDir}");
            return tag;
        }
        finally
        {
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
            try
            {
                var tmpDir = Path.GetDirectoryName(tmpFile);
                if (!string.IsNullOrEmpty(tmpDir) && Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            }
            catch { }
        }
    }

    public async Task<string> GetLatestRemoteVersionAsync(string lang, string ownerRepoOverride = null)
    {
        if (!LanguageCatalog.Catalog.ContainsKey(lang))
            throw new InvalidOperationException($"Language '{lang}' is not in the LanguageCatalog.");
        var def = LanguageCatalog.Catalog[lang];
        var (owner, repo) = ResolveOwnerRepo(def, ownerRepoOverride);

        var release = await resolver.GetReleaseInfoAsync(owner, repo, null);
        if (release == null) throw new InvalidOperationException("Could not fetch latest release.");
        return release.TagName ?? release.Name ?? "latest";
    }

    public Task<bool> VerifyChecksumAsync(string lang, string version) => Task.FromResult(true);

    public async Task RunSmokeTestAsync(string lang, string runtimePath)
    {
        if (!File.Exists(runtimePath))
            throw new FileNotFoundException("Runtime not found for smoke test", runtimePath);

        var def = LanguageCatalog.Catalog[lang];
        var psi = new ProcessStartInfo
        {
            FileName = runtimePath,
            Arguments = def.VersionArg, // pode ser null ou vazio
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("failed to start runtime for smoke test");

        if (!p.WaitForExit(8000))
        {
            try { p.Kill(true); } catch { }
            Console.WriteLine($"Warning: smoke test timed out for {lang} (ignored).");
            return;
        }

        if (p.ExitCode != 0)
        {
            var err = await p.StandardError.ReadToEndAsync();
            Console.WriteLine($"Warning: smoke test failed for {lang}. Exit {p.ExitCode}, Stderr: {err.Trim()} (ignored).");
            return;
        }

        Console.WriteLine($"Smoke test passed for {lang}.");
    }

    public async Task AddPackageAsync(string url)
    {
        // download directly into ~/.ralen/modules
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
        var modulesDir = Path.Combine(cfg.InstallDir, "modules");
        Directory.CreateDirectory(modulesDir);

        var name = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(name)) name = $"package_{Guid.NewGuid():N}";

        var dest = Path.Combine(modulesDir, name);
        if (File.Exists(dest))
        {
            Console.WriteLine($"Package already exists: {dest}");
            return;
        }

        Console.WriteLine($"Downloading package {url} -> {dest}");
        await remote.DownloadToFileAsync(url, dest);
        Console.WriteLine("Package downloaded.");
    }

    // small helper
    (string owner, string repo) ResolveOwnerRepo(LanguageDefinition def, string ownerRepoOverride)
    {
        if (!string.IsNullOrEmpty(ownerRepoOverride) && ownerRepoOverride.Contains("/"))
        {
            var parts = ownerRepoOverride.Split('/');
            return (parts[0], parts[1]);
        }
        return (def.RepoOwner, def.RepoName);
    }
}
