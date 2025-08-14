using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var cli = new InteractiveRalen();
            if (args != null && args.Length > 0)
            {
                // treat all args as a single command line string and run once
                var single = string.Join(' ', args);
                return await cli.ExecuteSingleCommand(single);
            }
            else
            {
                await cli.RunREPL();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fatal error: " + ex.Message);
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }
    }
}

/* ---------- Interactive shell ---------- */
class InteractiveRalen
{
    readonly Config config;
    readonly Installer installer;
    readonly ProjectManager pm;
    public InteractiveRalen()
    {
        config = Config.Load();
        installer = new Installer(config);
        pm = new ProjectManager(config);
    }

    public async Task RunREPL()
    {
        Console.WriteLine("ralen — interactive environment manager");
        Console.WriteLine("Type `help` to see commands. `exit` to exit.");
        // Prompt whether user wants to configure PATH now (manual)
        Console.Write("Do you want to set the user's PATH now to include the runtimes directory? (y/N) ");
        var k = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (k == "y" || k == "yes")
        {
            installer.ConfigurePathNow();
        }

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Equals("exit", StringComparison.OrdinalIgnoreCase) || line.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Exiting.");
                break;
            }
            await ExecuteSingleCommand(line);
        }
    }

    public async Task<int> ExecuteSingleCommand(string line)
    {
        var parts = SplitArgs(line).ToArray();
        if (parts.Length == 0) return 0;
        var cmd = parts[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "help":
                    PrintHelp();
                    return 0;
                case "install":
                    if (parts.Length >= 4 && parts[1].ToLowerInvariant() == "lang")
                    {
                        await installer.InstallLanguage(parts[2], parts[3]);
                        return 0;
                    }
                    Console.Error.WriteLine("Usage: install lang <owner/repo | LangName> <release | url>");
                    return 1;
                case "make":
                    if (parts.Length >= 2 && parts[1].ToLowerInvariant() == "console")
                    {
                        // parse options from the rest (--name X --lang Y --version Z)
                        var name = "NewApp"; var lang = "SaLang"; var version = "latest";
                        for (int i = 2; i < parts.Length; i++)
                        {
                            if (parts[i] == "--name" && i + 1 < parts.Length) { name = parts[++i]; continue; }
                            if (parts[i] == "--lang" && i + 1 < parts.Length) { lang = parts[++i]; continue; }
                            if (parts[i] == "--version" && i + 1 < parts.Length) { version = parts[++i]; continue; }
                        }
                        pm.CreateConsoleProject(Directory.GetCurrentDirectory(), name, lang, version);
                        return 0;
                    }
                    Console.Error.WriteLine("Usage: make console [--name X --lang Y --version Z]");
                    return 1;
                case "run":
                    {
                        var path = Directory.GetCurrentDirectory();
                        if (parts.Length >= 2) path = parts[1];
                        var proj = pm.FindProjectInDirectory(path);
                        if (proj == null)
                        {
                            Console.Error.WriteLine("No .ralenproj found in the specified directory.");
                            return 1;
                        }
                        var language = proj.GetLanguage();
                        var version = proj.GetVersion();
                        if (string.IsNullOrEmpty(language))
                        {
                            Console.Error.WriteLine("Language not specified in .ralenproj.");
                            return 1;
                        }
                        var runtimePath = installer.ResolveRuntimePath(language, version);
                        if (runtimePath == null)
                        {
                            Console.Error.WriteLine($"Runtime for language '{language}' version '{version}' not installed. Use 'install lang ...'.");
                            return 1;
                        }
                        Console.WriteLine($"Running with runtime: {runtimePath}");
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = runtimePath,
                            Arguments = $"\"{proj.ProjectFilePath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WorkingDirectory = Path.GetDirectoryName(proj.ProjectFilePath)
                        };
                        var proc = Process.Start(startInfo);
                        if (proc == null)
                        {
                            Console.Error.WriteLine("Failed to start process.");
                            return 1;
                        }
                        // stream output
                        _ = Task.Run(async () =>
                        {
                            string s;
                            while ((s = await proc.StandardOutput.ReadLineAsync()) != null) Console.WriteLine(s);
                        });
                        _ = Task.Run(async () =>
                        {
                            string s;
                            while ((s = await proc.StandardError.ReadLineAsync()) != null) Console.Error.WriteLine(s);
                        });
                        proc.WaitForExit();
                        return proc.ExitCode;
                    }
                case "add":
                    // add package <url>
                    if (parts.Length >= 3 && parts[1].ToLowerInvariant() == "package")
                    {
                        var url = parts[2];
                        await installer.AddPackage(url);
                        return 0;
                    }
                    Console.Error.WriteLine("Usage: add package <url>");
                    return 1;
                case "remove":
                    // remove package <filename>
                    if (parts.Length >= 3 && parts[1].ToLowerInvariant() == "package")
                    {
                        var name = parts[2];
                        installer.RemovePackage(name);
                        return 0;
                    }
                    Console.Error.WriteLine("Usage: remove package <filename>");
                    return 1;
                case "configure-path":
                case "configure-path-now":
                case "configurepath":
                    installer.ConfigurePathNow();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown command: {cmd}. Use 'help'.");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error executing command: " + ex.Message);
            return 2;
        }
    }

    void PrintHelp()
    {
        Console.WriteLine(@"
Commands (interactive mode):
    help
    install lang <owner/repo | LangName> <release | url>
    make console [--name <name>] [--lang <Language>] [--version <version>]
    run [<path_to_directory_or_file>]
    add package <url_to_file>       # downloads the file to <installRoot>/modules/ if it doesn't exist
    remove package <filename>       # removes the file from <installRoot>/modules/
    configure-path                  # adds the runtimes directory to the user's PATH
    exit
");
    }

    // simple token splitter — keeps quoted parts together
    IEnumerable<string> SplitArgs(string commandLine)
    {
        var cur = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (cur.Length > 0)
                {
                    yield return cur.ToString();
                    cur.Clear();
                }
            }
            else cur.Append(c);
        }
        if (cur.Length > 0) yield return cur.ToString();
    }
}

/* ---------- Configuration ---------- */
class Config
{
    public string InstallDir { get; set; } = DefaultInstallDir();
    public string DefaultGitHubOwner { get; set; } = "serene1491";
    public bool AutoAddToPath { get; set; } = false;

    public static string DefaultInstallDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".ralen");
    }

    public static Config Load()
    {
        try
        {
            var cfgPath = Path.Combine(DefaultInstallDir(), "config.json");
            if (!File.Exists(cfgPath))
            {
                var cfg = new Config();
                Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
                File.WriteAllText(cfgPath, System.Text.Json.JsonSerializer.Serialize(cfg, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return cfg;
            }
            var raw = File.ReadAllText(cfgPath);
            var obj = System.Text.Json.JsonSerializer.Deserialize<Config>(raw) ?? new Config();
            return obj;
        }
        catch
        {
            return new Config();
        }
    }

    public void Save()
    {
        var cfgPath = Path.Combine(DefaultInstallDir(), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
        File.WriteAllText(cfgPath, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}

/* ---------- Installer (instalar linguagens + packages) ---------- */
class Installer
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
        // configure PATH to include install root (user must have started interactively to accept)
        var addDir = Path.Combine(cfg.InstallDir); // include whole install dir so bin subfolders can be referenced
        AddToUserPath(addDir);
        Console.WriteLine($"Updated user PATH to include: {addDir}");
    }

    public async Task InstallLanguage(string repoOrName, string releaseOrUrl)
    {
        Directory.CreateDirectory(cfg.InstallDir);
        string downloadUrl;
        if (Uri.IsWellFormedUriString(releaseOrUrl, UriKind.Absolute))
        {
            downloadUrl = releaseOrUrl;
        }
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

/* ---------- Project Manager (.ralenproj) ---------- */
class ProjectManager
{
    readonly Config cfg;
    public ProjectManager(Config c) { cfg = c; }

    public void CreateConsoleProject(string cwd, string name, string lang, string version)
    {
        var dir = Path.Combine(cwd, name);
        Directory.CreateDirectory(dir);

        var proj = new XElement("Project",
            new XAttribute("Sdk", "Ralen.Project/1.0"),
            new XElement("PropertyGroup",
                new XElement("OutputType", "Exe"),
                new XElement("TargetFramework", lang),
                new XElement("RalenLanguage", lang),
                new XElement("RalenLanguageVersion", version)
            )
        );

        var projPath = Path.Combine(dir, $"{name}.ralenproj");
        proj.Save(projPath);

        var srcDir = Path.Combine(dir, "src");
        Directory.CreateDirectory(srcDir);
        var mainFile = Path.Combine(srcDir, "Program.ralen");
        var content = $"// Project {name}\nstd.print(\"Hello from {name} ({lang} {version})!\")\n";
        File.WriteAllText(mainFile, content);

        Console.WriteLine($"Project created: {projPath}");
    }

    public RalenProject FindProjectInDirectory(string dir)
    {
        string actual = dir;
        if (File.Exists(dir) && Path.GetExtension(dir).Equals(".ralenproj", StringComparison.OrdinalIgnoreCase))
        {
            return RalenProject.Load(Path.GetFullPath(dir));
        }
        if (!Directory.Exists(dir)) return null;
        var projFiles = Directory.GetFiles(dir, "*.ralenproj", SearchOption.TopDirectoryOnly);
        if (projFiles.Length == 0) return null;
        return RalenProject.Load(projFiles[0]);
    }
}

class RalenProject
{
    public string ProjectFilePath { get; private set; } = "";
    public XDocument Xml { get; private set; } = new XDocument();
    public static RalenProject Load(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            return new RalenProject { ProjectFilePath = path, Xml = doc };
        }
        catch
        {
            return null;
        }
    }

    public string GetLanguage()
    {
        var el = Xml.Descendants("RalenLanguage").FirstOrDefault();
        return el?.Value;
    }
    public string GetVersion()
    {
        var el = Xml.Descendants("RalenLanguageVersion").FirstOrDefault();
        return el?.Value;
    }
}
