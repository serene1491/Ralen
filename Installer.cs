using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Reflection;

public class Installer
{
    readonly Config cfg;
    readonly HttpClient http = new HttpClient();

    public Installer(Config config)
    {
        cfg = config;
        Directory.CreateDirectory(cfg.InstallDir);
        Directory.CreateDirectory(Path.Combine(cfg.InstallDir, "modules"));
    }

    public void ConfigurePathNow()
    {
        var addDir = Path.GetFullPath(cfg.InstallDir);
        Console.WriteLine($"Adding install root to user PATH and creating shim(s) as needed (install root = {addDir})...");
        TryEnsureUserBinAndCreateShim(addDir);
    }

    void TryEnsureUserBinAndCreateShim(string installRoot)
    {
        var exePath = GetCurrentExecutablePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            Console.WriteLine("Warning: could not find current executable path to create a shim.");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            EnsureWindowsUserBinAndShim(exePath);
        }
        else
        {
            EnsureUnixUserBinAndShim(exePath);
        }
    }

    static string GetCurrentExecutablePath()
    {
        // tenta diferentes formas de obter o caminho para o executável atual
        try
        {
            var asm = Assembly.GetEntryAssembly();
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
                var mod = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(mod)) return mod;
            }
            catch {}

            return null;
        }
        catch { return null; }
    }

    void EnsureUnixUserBinAndShim(string exePath)
    {
        var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathParts = envPath.Split(':').Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        string candidate = null;
        foreach (var p in pathParts)
        {
            var homef = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (p.StartsWith(homef) && Directory.Exists(p) && IsDirWritable(p))
            {
                candidate = p;
                break;
            }
        }

        // se não encontrou, preferir ~/.local/bin ou ~/bin
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localBin = Path.Combine(homeDir, ".local", "bin");
        var simpleBin = Path.Combine(homeDir, "bin");
        string targetBin;

        if (candidate != null) targetBin = candidate;
        else if (Directory.Exists(localBin) || TryCreateDir(localBin)) targetBin = localBin;
        else if (Directory.Exists(simpleBin) || TryCreateDir(simpleBin)) targetBin = simpleBin;
        else
        {
            // fallback: usar ~/.ralen (já adicionada ao profile pelos métodos antigos)
            var ralenBin = Path.Combine(homeDir, ".ralen");
            TryCreateDir(ralenBin);
            targetBin = ralenBin;
        }

        Console.WriteLine($"Using user bin directory: {targetBin}");

        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
        {
            var exeName = Path.GetFileName(exePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(exeName);
            var linkPath = Path.Combine(targetBin, nameWithoutExt);

            try
            {
                if (CreateSymlinkIfPossible(exePath, linkPath))
                    Console.WriteLine($"Created symlink: {linkPath} -> {exePath}");
                else
                {
                    CreateUnixWrapperScript(exePath, linkPath);
                    Console.WriteLine($"Created wrapper script: {linkPath} -> executes {exePath}");
                }

                try { Process.Start(new ProcessStartInfo { FileName = "chmod", Arguments = $"+x \"{linkPath}\"", UseShellExecute = false })?.WaitForExit(); } catch { }
            }
            catch (Exception ex){
                Console.WriteLine($"Warning creating shim: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Executable not found; shim won't be created. Make sure the binary is placed in the install dir or run manual symlink.");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var profile = Path.Combine(home, ".profile");
        var bashrc = Path.Combine(home, ".bashrc");
        var exportLine = $"export PATH=\"{targetBin}:$PATH\"";

        void AddIfMissing(string file)
        {
            try
            {
                if (!File.Exists(file)) File.WriteAllText(file, "# created by ralen\n");
                var content = File.ReadAllText(file);
                if (!content.Contains(exportLine))
                {
                    File.AppendAllText(file, "\n# ralen\n" + exportLine + "\n");
                }
            }
            catch { }
        }

        AddIfMissing(profile);
        AddIfMissing(bashrc);

        Console.WriteLine();
        Console.WriteLine("If the command is still not found in this session, run one of:");
        Console.WriteLine("  source ~/.profile");
        Console.WriteLine("or re-open your terminal. The shim was placed in: " + targetBin);
    }

    bool IsDirWritable(string dir)
    {
        try
        {
            var test = Path.Combine(dir, ".ralen_write_test_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(test, "x");
            File.Delete(test);
            return true;
        }
        catch { return false; }
    }

    bool TryCreateDir(string dir)
    {
        try { Directory.CreateDirectory(dir); return true; } catch { return false; }
    }

    bool CreateSymlinkIfPossible(string target, string linkPath)
    {
        try
        {
            if (File.Exists(linkPath) || Directory.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            // On Unix create symbolic link
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var result = MonoUnixSymlink(target, linkPath);
                return result;
            }
            else
            {
                // On Windows, try to create symlink (may require privilege)
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    bool MonoUnixSymlink(string target, string linkPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ln",
                Arguments = $"-sf \"{target}\" \"{linkPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var p = Process.Start(psi);
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    void CreateUnixWrapperScript(string exePath, string linkPath)
    {
        var script = $"#!/usr/bin/env sh\n\"{exePath}\" \"$@\"\n";
        File.WriteAllText(linkPath, script);
    }

    void EnsureWindowsUserBinAndShim(string exePath)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var winBin = Path.Combine(userProfile, "bin");
        TryCreateDir(winBin);

        string cmdName = "ralen";
        if (!string.IsNullOrEmpty(exePath))
        {
            var exeName = Path.GetFileName(exePath);
            cmdName = Path.GetFileNameWithoutExtension(exeName);
        }

        var cmdShimPath = Path.Combine(winBin, cmdName + ".cmd");

        try
        {
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var content = $"@echo off\r\n\"{exePath}\" %*\r\n";
                File.WriteAllText(cmdShimPath, content);
            }
            else
                Console.WriteLine("Executable not found; creating empty shim not possible.");

            var cur = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var parts = cur.Split(Path.PathSeparator).Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (!parts.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(winBin), StringComparison.OrdinalIgnoreCase)))
            {
                parts.Insert(0, winBin);
                var newPath = string.Join(Path.PathSeparator.ToString(), parts);
                Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);

                TryBroadcastEnvironmentChanged();
            }

            Console.WriteLine($"Created Windows shim: {cmdShimPath}");
            Console.WriteLine("If the command is not available immediately, you may need to restart your shell or sign out/in.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed creating Windows shim: " + ex.Message);
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    const int HWND_BROADCAST = 0xffff;
    const uint WM_SETTINGCHANGE = 0x001A;
    const uint SMTO_ABORTIFHUNG = 0x0002;

    void TryBroadcastEnvironmentChanged()
    {
        try
        {
            UIntPtr result;
            SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 5000, out result);
        }
        catch {}
    }

    public async Task InstallLanguage(string repoOrName, string releaseOrUrl)
    {
        Directory.CreateDirectory(cfg.InstallDir);
        string downloadUrl;
        if (Uri.IsWellFormedUriString(releaseOrUrl, UriKind.Absolute))
            downloadUrl = releaseOrUrl;
        else
        {
            string owner, repo;
            if (repoOrName.Contains("/"))
            {
                var parts = repoOrName.Split('/', 2);
                owner = parts[0]; repo = parts[1];
            }
            else
            {
                owner = cfg.DefaultGitHubOwner;
                repo = repoOrName;
            }

            var platformAssetName = GuessAssetName(repo, releaseOrUrl);
            var candidates = new List<string>();
            candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{releaseOrUrl}/{platformAssetName}");
            candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{releaseOrUrl}/{repo}-{releaseOrUrl}.zip");
            candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{releaseOrUrl}/{repo}-{releaseOrUrl}.tar.gz");
            candidates.Add($"https://github.com/{owner}/{repo}/releases/download/{releaseOrUrl}/{repo}.zip");
            candidates.Add($"https://github.com/{owner}/{repo}/archive/refs/tags/{releaseOrUrl}.zip");

            string found = null;
            foreach (var c in candidates)
            {
                if (await UrlExists(c))
                {
                    found = c; break;
                }
            }
            if (found == null)
            {
                Console.Error.WriteLine("The asset could not be located automatically. You can pass a direct URL to the release file (zip/tar.gz).");
                return;
            }
            downloadUrl = found;
        }

        Console.WriteLine("Downloading from: " + downloadUrl);
        var tmp = Path.Combine(Path.GetTempPath(), "ralen_download_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var downloaded = Path.Combine(tmp, Path.GetFileName(new Uri(downloadUrl).LocalPath));
        using (var resp = await http.GetAsync(downloadUrl))
        {
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Download failed: {resp.StatusCode}");
                return;
            }
            using (var fs = new FileStream(downloaded, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await resp.Content.CopyToAsync(fs);
            }
        }

        var lang = repoOrName.Contains('/') ? repoOrName.Split('/').Last() : repoOrName;
        var release = ExtractReleaseFromInput(releaseOrUrl);
        var dest = Path.Combine(cfg.InstallDir, lang, release);
        if (Directory.Exists(dest))
        {
            Console.WriteLine($"Version already installed on {dest}, removing and reinstalling.");
            try { Directory.Delete(dest, true); } catch { }
        }
        Directory.CreateDirectory(dest);

        if (downloaded.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(downloaded, dest);
        }
        else if (downloaded.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || downloaded.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            if (!RunTarExtract(downloaded, dest))
            {
                Console.Error.WriteLine("Failed to extract tar.gz: No tar tools available.");
                return;
            }
        }
        else
        {
            File.Move(downloaded, Path.Combine(dest, Path.GetFileName(downloaded)));
        }

        var runtime = FindRuntimeBinary(dest, lang);
        if (runtime != null)
        {
            Console.WriteLine($"Localized runtime: {runtime}");
            if (cfg.AutoAddToPath)
            {
                AddToUserPath(Path.GetDirectoryName(runtime)!);
                Console.WriteLine("Added to user's PATH (config AutoAddToPath).");
            }
        }
        else
        {
            Console.WriteLine("No runtime executables found automatically. Check the installation directory.");
        }

        var manifest = new InstalledManifest { Language = lang, Release = release, InstalledAt = DateTime.UtcNow };
        var manifestPath = Path.Combine(dest, "installed.json");
        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Installation completed on: {dest}");
    }

    public async Task AddPackage(string url)
    {
        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            Console.Error.WriteLine("Invalid URL.");
            return;
        }

        var modulesDir = Path.Combine(cfg.InstallDir, "modules");
        Directory.CreateDirectory(modulesDir);

        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(fileName))
        {
            Console.Error.WriteLine("Could not derive filename from URL.");
            return;
        }

        var dest = Path.Combine(modulesDir, fileName);
        if (File.Exists(dest))
        {
            Console.WriteLine($"Package '{fileName}' already exists in {modulesDir}. Nothing to do.");
            return;
        }

        Console.WriteLine($"Downloading package for: {dest}");
        using (var resp = await http.GetAsync(url))
        {
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Download failed: {resp.StatusCode}");
                return;
            }
            using (var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await resp.Content.CopyToAsync(fs);
            }
        }
        Console.WriteLine("Package added.");
    }

    public void RemovePackage(string filename)
    {
        var modulesDir = Path.Combine(cfg.InstallDir, "modules");
        var target = Path.Combine(modulesDir, filename);
        if (!File.Exists(target))
        {
            Console.WriteLine($"File '{filename}' not found in {modulesDir}.");
            return;
        }
        try
        {
            File.Delete(target);
            Console.WriteLine($"File '{filename}' removed.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to remove file: " + ex.Message);
        }
    }

    string GuessAssetName(string repo, string release)
    {
        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        return $"{repo}-{release}-{platform}-{arch}.zip";
    }

    string ExtractReleaseFromInput(string releaseOrUrl)
    {
        var m = Regex.Match(releaseOrUrl, @"/download/([^/]+)/");
        if (m.Success) return m.Groups[1].Value;
        return releaseOrUrl;
    }

    async Task<bool> UrlExists(string url)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Head, url);
            var resp = await http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    bool RunTarExtract(string archive, string dest)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $" -xzf \"{archive}\" -C \"{dest}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var p = Process.Start(psi);
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    string FindRuntimeBinary(string root, string lang)
    {
        var candidates = new List<string>();
        var entries = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        foreach (var f in entries)
        {
            var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (name == lang.ToLowerInvariant()) candidates.Add(f);
            if (name == lang.ToLowerInvariant().Replace(" ", "")) candidates.Add(f);
            if (name.Contains(lang.ToLowerInvariant())) candidates.Add(f);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ext == ".exe") candidates.Add(f);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (string.IsNullOrEmpty(ext) || ext == ".sh")) candidates.Add(f);
        }

        var preferred = candidates
            .OrderByDescending(p => p.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("bin", StringComparison.OrdinalIgnoreCase)))
            .ThenBy(p => p.Length)
            .FirstOrDefault();

        if (preferred != null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try { Process.Start(new ProcessStartInfo { FileName = "chmod", Arguments = $"+x \"{preferred}\"", UseShellExecute = false })?.WaitForExit(); }
                catch { }
            }
        }

        return preferred;
    }

    public string ResolveRuntimePath(string language, string release)
    {
        var baseDir = Path.Combine(cfg.InstallDir, language, release);
        if (!Directory.Exists(baseDir))
        {
            var baseLangDir = Path.Combine(cfg.InstallDir, language);
            if (!Directory.Exists(baseLangDir)) return null;
            var sub = new DirectoryInfo(baseLangDir).GetDirectories().OrderByDescending(d => d.LastWriteTimeUtc).FirstOrDefault();
            if (sub == null) return null;
            baseDir = sub.FullName;
        }

        var bin = FindRuntimeBinary(baseDir, language);
        if (bin != null) return bin;

        var binDir = Path.Combine(baseDir, "bin");
        if (Directory.Exists(binDir))
        {
            var possible = Directory.EnumerateFiles(binDir).FirstOrDefault();
            if (possible != null) return possible;
        }

        return null;
    }

    void AddToUserPath(string dir)
    {
        dir = Path.GetFullPath(dir);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var cur = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var parts = cur.Split(Path.PathSeparator).Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (!parts.Contains(dir, StringComparer.OrdinalIgnoreCase))
            {
                parts.Insert(0, dir);
                var newPath = string.Join(Path.PathSeparator.ToString(), parts);
                Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var profile = Path.Combine(home, ".profile");
            var bashrc = Path.Combine(home, ".bashrc");
            var exportLine = $"export PATH=\"{dir}:$PATH\"";

            void AddIfMissing(string file)
            {
                try
                {
                    if (!File.Exists(file)) File.WriteAllText(file, "# created by ralen\n");
                    var content = File.ReadAllText(file);
                    if (!content.Contains(exportLine))
                    {
                        File.AppendAllText(file, "\n# ralen\n" + exportLine + "\n");
                    }
                }
                catch { }
            }

            AddIfMissing(profile);
            AddIfMissing(bashrc);
        }
    }

    class InstalledManifest
    {
        public string Language { get; set; } = "";
        public string Release { get; set; } = "";
        public DateTime InstalledAt { get; set; }
    }
}
