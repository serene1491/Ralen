using System.Diagnostics;
using System.Text.Json;
namespace Ralen;

public class RalenSdk
{
    readonly IInstaller installer;
    readonly RuntimeRegistry registry;

    public RalenSdk(IInstaller inst, RuntimeRegistry reg) { installer = inst; registry = reg; }

    public async Task<int> RunAsync(string pathOrDir, string explicitProjectFile = null, bool interactiveIfMultiple = false)
    {
        string dir = pathOrDir;
        if (File.Exists(pathOrDir) && Path.GetExtension(pathOrDir).Equals(".ralenproj", StringComparison.OrdinalIgnoreCase))
            dir = Path.GetDirectoryName(pathOrDir)!;

        var projects = ProjectResolver.FindProjectsInDirectory(dir).ToArray();
        ProjectDescriptor project = null;

        if (!string.IsNullOrEmpty(explicitProjectFile))
        {
            var projPath = Path.GetFullPath(explicitProjectFile);
            project = projects.FirstOrDefault(p => Path.GetFullPath(p.ProjectFilePath) == projPath);
            if (project == null) return Error($"Specified project file not found in directory: {explicitProjectFile}");
        }
        else if (projects.Length == 0)
            return Error("No .ralenproj found in the specified directory.");
        else if (projects.Length == 1)
            project = projects[0];
        else
        {
            // multiple found
            if (interactiveIfMultiple && Console.IsInputRedirected == false)
            {
                Console.WriteLine("Multiple .ralenproj files found — choose one:");
                for (int i = 0; i < projects.Length; i++)
                    Console.WriteLine($"  [{i}] {projects[i].ProjectFilePath} ({projects[i].Language} {projects[i].Version})");
                Console.Write("Choice: ");
                var line = Console.ReadLine();
                if (!int.TryParse(line, out var idx) || idx < 0 || idx >= projects.Length)
                    return Error("Invalid selection.");
                project = projects[idx];
            }
            else
            {
                Console.Error.WriteLine("Multiple .ralenproj files found:");
                foreach (var p in projects) Console.Error.WriteLine("   " + p.ProjectFilePath);
                Console.Error.WriteLine("Call with --project <file> to specify which one.");
                return 2;
            }
        }

        if (project == null) return Error("Project resolution failed.");

        if (string.IsNullOrWhiteSpace(project.Language))
            return Error("Language not specified in project descriptor.");

        if (!LanguageCatalog.Catalog.ContainsKey(project.Language))
            return Error($"Language '{project.Language}' is not a supported ralen language.");

        var requestedVersion = project.Version ?? "latest";
        string resolvedVersion;
        try
        {
            resolvedVersion = await installer.EnsureInstalledAsync(project.Language, requestedVersion);
        }
        catch (Exception ex)
        {
            return Error($"Failed to ensure runtime installed: {ex.Message}");
        }

        if (LanguageCatalog.Catalog[project.Language].KnownBadVersions.Contains(resolvedVersion))
        {
            Console.Error.WriteLine($"Warning: version {resolvedVersion} of {project.Language} is known to be problematic.");
        }

        var runtimePath = registry.GetPath(project.Language, resolvedVersion);
        if (!File.Exists(runtimePath))
            return Error($"Runtime binary not found at expected path: {runtimePath}");

        // optional verify + smoke test
        try
        {
            if (!await installer.VerifyChecksumAsync(project.Language, resolvedVersion))
                Console.Error.WriteLine("Warning: checksum verification failed (or skipped).");
            await installer.RunSmokeTestAsync(project.Language, runtimePath);
        }
        catch (Exception ex)
        {
            return Error($"Runtime smoke test failed: {ex.Message}");
        }

        // Determinar arquivo de entrada
        var entryFile = !string.IsNullOrWhiteSpace(project.EntryFile)
            ? Path.Combine(Path.GetDirectoryName(project.ProjectFilePath)!, project.EntryFile)
            : Path.Combine(Path.GetDirectoryName(project.ProjectFilePath)!, "main.sal");

        // Garantir que o arquivo exista
        if (!File.Exists(entryFile))
        {
            File.WriteAllText(entryFile, $"// example entry for {project.Language}\nconsole.log('hello ralen');");
        }

        // Construir args: apenas o path do arquivo
        var args = $"\"{entryFile}\"";

        var psi = new ProcessStartInfo
        {
            FileName = runtimePath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(entryFile)
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start runtime process");

        // stream stdout/stderr
        var tOut = Task.Run(async () => { string s; while ((s = await proc.StandardOutput.ReadLineAsync()) != null) Console.WriteLine(s); });
        var tErr = Task.Run(async () => { string s; while ((s = await proc.StandardError.ReadLineAsync()) != null) Console.Error.WriteLine(s); });
        await Task.WhenAll(tOut, tErr, Task.Run(() => proc.WaitForExit()));

        return proc.ExitCode;
    }

    int Error(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    public async Task<int> CreateConsoleAsync(string destDir, string name, string language, string versionSpec = "latest")
    {
        if (!LanguageCatalog.Catalog.ContainsKey(language))
            return Error($"Unknown language {language}.");

        // resolve version
        string resolved;
        try { resolved = await installer.EnsureInstalledAsync(language, versionSpec); }
        catch (Exception ex) { return Error("Failed to resolve/install runtime: " + ex.Message); }

        // create project file
        Directory.CreateDirectory(destDir);
        var proj = new {
            name,
            language,
            version = resolved,
            entry = "main." + (language == "salang" ? "sr" : "js")
        };
        var projPath = Path.Combine(destDir, $"{name}.ralenproj");
        File.WriteAllText(projPath, JsonSerializer.Serialize(proj, new JsonSerializerOptions{ WriteIndented = true }));
        // create example entry
        var entry = Path.Combine(destDir, proj.entry);
        if (!File.Exists(entry))
            File.WriteAllText(entry, $"// example entry for {language}\nconsole.log('hello ralen');");
        Console.WriteLine($"Project created at {projPath} using {language} {resolved}.");
        return 0;
    }
}
