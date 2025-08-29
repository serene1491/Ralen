using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Ralen;

public class UserPathConfigurator
{
    readonly Config cfg;

    public UserPathConfigurator(Config config)
    {
        cfg = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Configura o diretório de shims do ralen no PATH do usuário e (opcionalmente) cria shims/wrappers.
    /// IMPORTANT: adiciona o diretório dos shims (onde o comando 'ralen' ficará), não os diretórios de runtimes/versões.
    /// </summary>
    /// <param name="addToPath">Se true, adiciona o diretório de shims ao PATH do usuário.</param>
    /// <param name="createShims">Se true, cria symlink/wrapper/shim apontando para o executável atual.</param>
    public void ConfigurePathNow(bool addToPath = true, bool createShims = true)
    {
        var exePath = GetCurrentExecutablePath();
        var exeDir = exePath is not null ? Path.GetDirectoryName(exePath) ?? cfg.InstallDir : cfg.InstallDir;
        Console.WriteLine($"Configuring ralen shim directory and (optionally) creating shims. Shim directory will be used in PATH (not runtime/version directories). Shim directory: {exeDir}");
        TryEnsureUserBinAndCreateShim(exeDir, addToPath, createShims);
    }

    void TryEnsureUserBinAndCreateShim(string shimSourceDir, bool addToPath, bool createShims)
    {
        var exePath = GetCurrentExecutablePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            Console.WriteLine("Warning: could not find current executable path to create a shim. Will still add shim directory to PATH if requested.");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            EnsureWindowsUserBinAndShim(exePath, shimSourceDir, addToPath, createShims);
        else
            EnsureUnixUserBinAndShim(exePath, shimSourceDir, addToPath, createShims);
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
                var mod = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(mod)) return mod;
            }
            catch { /* ignore */ }

            return null;
        }
        catch { return null; }
    }

    void EnsureUnixUserBinAndShim(string exePath, string shimSourceDir, bool addToPath, bool createShims)
    {
        // Determina diretório de shims do usuário (onde colocaremos enlaces/wrapper scripts)
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

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localBin = Path.Combine(homeDir, ".local", "bin");
        var simpleBin = Path.Combine(homeDir, "bin");
        string targetBin;

        if (candidate != null) targetBin = candidate;
        else if (Directory.Exists(localBin) || TryCreateDir(localBin)) targetBin = localBin;
        else if (Directory.Exists(simpleBin) || TryCreateDir(simpleBin)) targetBin = simpleBin;
        else
        {
            var ralenBin = Path.Combine(homeDir, ".ralen", "bin"); // central shim dir
            TryCreateDir(ralenBin);
            targetBin = ralenBin;
        }

        Console.WriteLine($"User shim directory (will be used in PATH): {targetBin}");

        if (createShims)
        {
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                // Cria shim apontando para o executável atual (ralen)
                var exeName = Path.GetFileName(exePath);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(exeName).ToLowerInvariant();
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
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning creating shim: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Executable not found; shim won't be created. You can still add the shim directory to PATH and later place a shim named 'ralen' there.");
            }
        }
        else
        {
            Console.WriteLine("Skipping shim creation (createShims=false).");
        }

        if (addToPath)
        {
            // Adiciona apenas o diretório de shims ao PATH (.profile/.bashrc)
            var profile = Path.Combine(homeDir, ".profile");
            var bashrc = Path.Combine(homeDir, ".bashrc");
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
        else
        {
            Console.WriteLine("Skipping PATH modification (addToPath=false).");
        }
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

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return MonoUnixSymlink(target, linkPath);
            }
            else
            {
                // Windows symlink creation might require elevation; fallback to shim creation handled elsewhere
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
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode == 0;
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

    void EnsureWindowsUserBinAndShim(string exePath, string shimSourceDir, bool addToPath, bool createShims)
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
            if (createShims)
            {
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    var content = $"@echo off\r\n\"{exePath}\" %*\r\n";
                    File.WriteAllText(cmdShimPath, content);
                    Console.WriteLine($"Created Windows shim: {cmdShimPath}");
                }
                else
                    Console.WriteLine("Executable not found; creating Windows shim not possible. You can place a shim in the shim directory manually later.");
            }
            else
            {
                Console.WriteLine("Skipping Windows shim creation (createShims=false).");
            }

            if (addToPath)
            {
                // Adiciona apenas o diretório de shims (winBin) ao PATH do usuário
                var cur = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
                var parts = cur.Split(Path.PathSeparator).Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (!parts.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(winBin), StringComparison.OrdinalIgnoreCase)))
                {
                    parts.Insert(0, winBin);
                    var newPath = string.Join(Path.PathSeparator.ToString(), parts);
                    Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);

                    TryBroadcastEnvironmentChanged();
                }

                Console.WriteLine("Added shim directory to user PATH (Windows).");
                Console.WriteLine("If the command is not available immediately, you may need to restart your shell or sign out/in.");
            }
            else
            {
                Console.WriteLine("Skipping PATH modification on Windows (addToPath=false).");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed creating Windows shim: " + ex.Message);
        }
    }

    // P/Invoke for broadcasting environment change on Windows
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
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
            Thread.Sleep(200);
        }
        catch { }
    }
}
