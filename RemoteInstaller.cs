using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
namespace Ralen;

public class RemoteInstaller
{
    readonly HttpClient http;

    public RemoteInstaller(HttpClient httpClient)
    {
        http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> DownloadToTempFileAsync(string downloadUrl, string lang)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl)) throw new ArgumentNullException(nameof(downloadUrl));
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri)) throw new InvalidOperationException($"Download URL is not an absolute URI: '{downloadUrl}'");

        var tmpDir = Path.Combine(Path.GetTempPath(), $"ralen_{lang}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        // try filename from URI
        string name = Path.GetFileName(downloadUri.LocalPath);
        if (string.IsNullOrEmpty(name)) name = $"download_{Guid.NewGuid():N}";
        var tmpFile = Path.Combine(tmpDir, name);

        using var resp = await http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        // use Content-Disposition filename if present
        if (resp.Content.Headers.ContentDisposition?.FileNameStar != null)
        {
            var suggested = resp.Content.Headers.ContentDisposition.FileNameStar.Trim('"');
            var f = Path.GetFileName(suggested);
            if (!string.IsNullOrEmpty(f)) tmpFile = Path.Combine(tmpDir, f);
        }

        using var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None);
        await resp.Content.CopyToAsync(fs);
        return tmpFile;
    }

    public async Task DownloadToFileAsync(string url, string destinationPath)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await resp.Content.CopyToAsync(fs);
    }

    public async Task InstallDownloadedAsync(string downloadedPath, string versionDir, LanguageDefinition def)
    {
        string binDir = Path.Combine(versionDir, "bin");
        Directory.CreateDirectory(binDir);

        if (IsZipFile(downloadedPath))
        {
            ZipFile.ExtractToDirectory(downloadedPath, versionDir);
            NormalizeExtractedFolder(versionDir);

            // Procura executável dentro da extração e normaliza
            EnsureRuntimeHasBin(def.Key, versionDir, def);

            // Renomeia para o nome esperado (lowercase)
            var exePath = Directory.GetFiles(binDir)
                                .FirstOrDefault(f => !Directory.Exists(f) && Path.GetFileName(f).ToLower().Contains(def.Key.ToLower()));
            if (exePath != null)
            {
                var dest = Path.Combine(binDir, def.Key.ToLower());
                if (!File.Exists(dest)) File.Move(exePath, dest);
                MakeExecutableIfUnix(dest);
            }
        }
        else
        {
            // arquivo único
            var dest = Path.Combine(binDir, def.Key.ToLower());
            File.Move(downloadedPath, dest);
            MakeExecutableIfUnix(dest);
        }

        await Task.CompletedTask;
    }

    // Faz o arquivo executável no Unix
    static void MakeExecutableIfUnix(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var psi = new ProcessStartInfo("chmod", $"+x \"{path}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p.WaitForExit();
            }
            catch { }
        }
    }

    static bool IsZipFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".zip") return true;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length >= 2)
            {
                var b = new byte[4];
                int read = fs.Read(b, 0, Math.Min(b.Length, (int)fs.Length));
                if (read >= 2 && b[0] == 0x50 && b[1] == 0x4B) return true;
            }
        }
        catch { }
        return false;
    }

    static void NormalizeExtractedFolder(string versionDir)
    {
        var entries = Directory.GetFileSystemEntries(versionDir);
        if (entries.Length == 1 && Directory.Exists(entries[0]))
        {
            var sub = entries[0];
            foreach (var f in Directory.GetFiles(sub, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sub, f);
                var dest = Path.Combine(versionDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(f, dest);
            }
            Directory.Delete(sub, true);
        }
    }

    static void EnsureRuntimeHasBin(string lang, string versionDir, LanguageDefinition def)
    {
        var binDir = Path.Combine(versionDir, "bin");
        if (Directory.Exists(binDir) && Directory.EnumerateFileSystemEntries(binDir).Any()) return;

        var names = def.RuntimeExecutables.Select(n => n.ToLowerInvariant()).ToHashSet();
        var candidates = Directory.GetFiles(versionDir, "*", SearchOption.AllDirectories)
            .Where(f => names.Contains(Path.GetFileName(f).ToLowerInvariant()))
            .ToArray();

        Directory.CreateDirectory(binDir);

        if (candidates.Length == 0)
        {
            // fallback: pick first file at root
            var files = Directory.GetFiles(versionDir).ToArray();
            var exe = files.FirstOrDefault();
            if (exe != null)
            {
                var dest = Path.Combine(binDir, Path.GetFileName(exe));
                File.Move(exe, dest);
                MakeExecutableIfUnix(dest);
            }
            return;
        }

        foreach (var c in candidates)
        {
            var destName = Path.GetFileName(c).ToLowerInvariant();
            var dest = Path.Combine(binDir, destName);
            if (!File.Exists(dest))
            {
                File.Move(c, dest);
                MakeExecutableIfUnix(dest);
            }
        }
    }
}
