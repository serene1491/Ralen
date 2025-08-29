namespace Ralen;

public class Program
{
    static async Task<int> Main(string[] args)
    {
        var cfg = Config.Load();
        var installRoot = cfg.InstallDir;
        var registry = new RuntimeRegistry(installRoot);
        var installer = new SimpleInstaller(registry, cfg);
        var sdk = new RalenSdk(installer, registry);

        if (args.Length == 0)
        {
            var shell = new InteractiveShell(sdk);
            await shell.RunAsync();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        try
        {
            switch (cmd)
            {
                case "run":
                    return await HandleRun(args, sdk);

                case "create":
                case "new":
                case "make":
                    return await HandleCreate(args, sdk);

                case "install-lang":
                case "install":
                    return await HandleInstall(args, installer, registry);

                case "list-known":
                    {
                        Console.WriteLine("Known ralen languages:");
                        foreach (var (k, def) in LanguageCatalog.Catalog.OrderBy(kv => kv.Key))
                            Console.WriteLine($"  {k}  (repo: {def.RepoOwner}/{def.RepoName})");
                        return 0;
                    }

                case "list-installed":
                    return HandleListInstalled(args, registry);

                case "add-to-path":
                case "configure-path":
                    {
                        var createShims = args.Skip(1).Any(a => a == "--no-shim") ? false : true;
                        installer.ConfigurePathNow(addToPath: true, createShims: createShims);
                        Console.WriteLine("Done. (added shim dir to PATH)");
                        return 0;
                    }

                case "remove-from-path":
                    {
                        var removeShim = !args.Skip(1).Any(a => a == "--keep-shim");
                        RemoveShimDirFromPath(removeShim);
                        Console.WriteLine("Done. (removed shim dir from PATH)");
                        return 0;
                    }

                case "add-package":
                    return await HandleAddPackage(args, cfg, installer);

                case "remove-package":
                    return HandleRemovePackage(args, cfg);

                default:
                    Console.Error.WriteLine("Unknown command. Available: run, create, install-lang, list-known, list-installed, add-to-path, remove-from-path, add-package, remove-package");
                    return 1;
            }
        }
        catch (InvalidOperationException inv)
        {
            Console.Error.WriteLine("Error: " + inv.Message);
            Console.Error.WriteLine("Sugestões:");
            Console.Error.WriteLine("  - Verifique se o repo/tag existe no GitHub.");
            Console.Error.WriteLine("  - Tente: install-lang <lang> owner/repo <version>");
            Console.Error.WriteLine("  - Ou instale localmente: coloque um zip com o runtime em ~/.ralen/versions/<lang>/<version>/bin/");
            return 2;
        }
        catch (HttpRequestException hr)
        {
            Console.Error.WriteLine("Network / GitHub request failed: " + hr.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 2;
        }
    }

    #region Handlers (refatorados)

    static async Task<int> HandleRun(string[] args, RalenSdk sdk)
    {
        var path = args.Length >= 2 ? args[1] : Directory.GetCurrentDirectory();
        string projFile = null;
        bool interactive = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--project" && i + 1 < args.Length) projFile = args[++i];
            if (args[i] == "--interactive") interactive = true;
        }
        return await sdk.RunAsync(path, projFile, interactive);
    }

    static async Task<int> HandleCreate(string[] args, RalenSdk sdk)
    {
        string name = "NewApp", lang = "salang", version = "latest", dir = Directory.GetCurrentDirectory();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--name" && i + 1 < args.Length) name = args[++i];
            if (args[i] == "--lang" && i + 1 < args.Length) lang = args[++i];
            if (args[i] == "--version" && i + 1 < args.Length) version = args[++i];
            if (args[i] == "--dir" && i + 1 < args.Length) dir = args[++i];
        }
        return await sdk.CreateConsoleAsync(dir, name, lang, version);
    }

    static int HandleListInstalled(string[] args, RuntimeRegistry registry)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: list-installed <lang>");
            return 1;
        }
        var lang = args[1];
        var installed = registry.ListInstalledVersions(lang).ToArray();
        if (!installed.Any())
        {
            Console.WriteLine($"No versions installed for language '{lang}'.");
            return 0;
        }
        Console.WriteLine($"Installed versions for {lang}:");
        foreach (var v in installed) Console.WriteLine($"  {v}");
        return 0;
    }

    static async Task<int> HandleAddPackage(string[] args, Config cfg, SimpleInstaller installer)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: add-package <url> [--project <dir>]");
            return 1;
        }
        var url = args[1];
        string projectDir = null;
        for (int i = 2; i < args.Length; i++)
            if (args[i] == "--project" && i + 1 < args.Length) projectDir = args[++i];

        if (string.IsNullOrEmpty(projectDir))
        {
            await installer.AddPackageAsync(url);
            return 0;
        }
        else
        {
            var modDir = Path.Combine(projectDir, "modules");
            Directory.CreateDirectory(modDir);
            await DownloadTo(url, modDir);
            Console.WriteLine($"Package downloaded to {modDir}");
            return 0;
        }
    }

    static int HandleRemovePackage(string[] args, Config cfg)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: remove-package <filename> [--project <dir>]");
            return 1;
        }
        var name = args[1];
        string projectDir = null;
        for (int i = 2; i < args.Length; i++)
            if (args[i] == "--project" && i + 1 < args.Length) projectDir = args[++i];

        string baseDir = string.IsNullOrEmpty(projectDir) ? Path.Combine(cfg.InstallDir, "modules") : Path.Combine(projectDir, "modules");
        var path = Path.Combine(baseDir, name);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Package not found: {path}");
            return 1;
        }
        File.Delete(path);
        Console.WriteLine($"Deleted {path}");
        return 0;
    }

    #endregion

    static async Task<int> HandleInstall(string[] args, SimpleInstaller installer, RuntimeRegistry registry)
    {
        // usage: install-lang <lang> [owner/repo | owner repo] [version]
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: install <lang|all> [owner/repo | owner repo] [version]");
            return 1;
        }

        var target = args[1];
        string ownerRepo = null;
        string version = "latest";

        // Flexible parsing with clearer rules:
        // - install-lang lang
        // - install-lang lang version
        // - install-lang lang owner repo
        // - install-lang lang owner/repo [version]
        // - install-lang lang owner version  (owner + version)   <-- common case you used

        if (args.Length >= 3)
        {
            var a2 = args[2];

            // a2 contains a slash => owner/repo
            if (a2.Contains("/"))
            {
                ownerRepo = a2;
                if (args.Length >= 4 && LooksLikeVersion(args[3])) version = args[3];
            }
            else if (args.Length >= 4 && !args[3].Contains("/"))
            {
                // Could be: owner repo  OR  owner version
                var a3 = args[3];
                if (LooksLikeVersion(a3))
                {
                    // treat as owner + version: owner <space> version
                    ownerRepo = $"{a2}/{target.ToLowerInvariant()}"; // assume repo name same as language key
                    version = a3;
                }
                else
                {
                    // treat as owner + repo
                    ownerRepo = $"{a2}/{a3}";
                    if (args.Length >= 5 && LooksLikeVersion(args[4])) version = args[4];
                }
            }
            else
            {
                // args.Length == 3 and a2 either a version or an owner without repo
                if (LooksLikeVersion(a2))
                {
                    version = a2;
                }
                else
                {
                    // owner only — assume repo name from language key
                    ownerRepo = $"{a2}/{target.ToLowerInvariant()}";
                    if (args.Length >= 4 && LooksLikeVersion(args[3])) version = args[3];
                }
            }
        }

        // quick normalize
        if (!string.IsNullOrEmpty(ownerRepo))
            ownerRepo = ownerRepo.Trim();

        try
        {
            if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var lang in LanguageCatalog.Catalog.Keys)
                {
                    Console.WriteLine($"Installing {lang}...");
                    await installer.EnsureInstalledAsync(lang, version, ownerRepo);
                }
                return 0;
            }
            else
            {
                var langKey = target.ToLowerInvariant();
                if (!LanguageCatalog.Catalog.ContainsKey(langKey))
                {
                    Console.Error.WriteLine($"Unknown language '{target}'. Use 'list-known' to see supported languages.");
                    return 1;
                }

                // If ownerRepo provided, print it; else we'll use def defaults
                if (!string.IsNullOrEmpty(ownerRepo))
                    Console.WriteLine($"Using owner/repo override: {ownerRepo}  version: {version}");
                else
                {
                    var def = LanguageCatalog.Catalog[langKey];
                    Console.WriteLine($"Using default repo: {def.RepoOwner}/{def.RepoName}  version: {version}");
                }

                await installer.EnsureInstalledAsync(langKey, version, ownerRepo);
                return 0;
            }
        }
        catch (InvalidOperationException)
        {
            throw; // let outer handler print suggestions
        }
    }

    #region Helpers re-used

    static void RemoveShimDirFromPath(bool removeShimFile)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            var winBin = Path.Combine(home, "bin");
            var cur = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var parts = cur.Split(Path.PathSeparator).Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
            var normalized = parts.Select(Path.GetFullPath).ToList();
            var toRemove = normalized.FirstOrDefault(p => string.Equals(p, Path.GetFullPath(winBin), StringComparison.OrdinalIgnoreCase));
            if (toRemove != null)
            {
                parts = parts.Where(p => !string.Equals(Path.GetFullPath(p), toRemove, StringComparison.OrdinalIgnoreCase)).ToList();
                var newPath = string.Join(Path.PathSeparator.ToString(), parts);
                Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
            }

            if (removeShimFile)
            {
                var exeName = Path.GetFileNameWithoutExtension(GetCurrentExecutablePath() ?? "ralen");
                var file1 = Path.Combine(winBin, exeName + ".cmd");
                try { if (File.Exists(file1)) File.Delete(file1); } catch { }
            }

            Console.WriteLine($"Removed {winBin} from user PATH (if present).");
        }
        else
        {
            var localBin = Path.Combine(home, ".local", "bin");
            var simpleBin = Path.Combine(home, "bin");
            var ralenBin = Path.Combine(home, ".ralen", "bin");
            string[] candidates = { localBin, simpleBin, ralenBin };
            string target = candidates.FirstOrDefault(Directory.Exists) ?? ralenBin;

            void RemoveFromFile(string file)
            {
                try
                {
                    if (!File.Exists(file)) return;
                    var lines = File.ReadAllLines(file).ToList();
                    var filtered = lines.Where(l => !l.Contains($"export PATH=\"{target}:$PATH\"") && !l.Contains(target)).ToArray();
                    if (filtered.Length != lines.Count)
                        File.WriteAllLines(file, filtered);
                }
                catch { }
            }

            RemoveFromFile(Path.Combine(home, ".profile"));
            RemoveFromFile(Path.Combine(home, ".bashrc"));

            if (removeShimFile)
            {
                var exeName = Path.GetFileNameWithoutExtension(GetCurrentExecutablePath() ?? "ralen");
                var linkPath = Path.Combine(target, exeName);
                try { if (File.Exists(linkPath) || Directory.Exists(linkPath)) File.Delete(linkPath); } catch { }
            }

            Console.WriteLine($"Attempted to remove PATH additions referencing {target} (if present). You may need to restart your shell.");
        }
    }

    static string GetCurrentExecutablePath()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly();
            var loc = asm?.Location;
            if (!string.IsNullOrEmpty(loc)) return loc;

            var env0 = Environment.GetCommandLineArgs().FirstOrDefault();
            if (!string.IsNullOrEmpty(env0))
            {
                var full = Path.GetFullPath(env0);
                if (File.Exists(full)) return full;
            }

            try
            {
                var mod = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(mod)) return mod;
            }
            catch { }

            return null;
        }
        catch { return null; }
    }

    static async Task DownloadTo(string url, string destDir)
    {
        Directory.CreateDirectory(destDir);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ralen-client/1.0");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var name = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(name)) name = $"package_{Guid.NewGuid():N}";
        var dest = Path.Combine(destDir, name);
        using var fs = new FileStream(dest, FileMode.CreateNew);
        await resp.Content.CopyToAsync(fs);
    }

    static bool LooksLikeVersion(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(s, @"^v?\d+(\.\d+)*(-.+)?$");
    }

    #endregion
}
