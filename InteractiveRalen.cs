using System.Text;
using System.Diagnostics;
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
