namespace Ralen;

public class InteractiveShell
{
    readonly RalenSdk sdk;
    public InteractiveShell(RalenSdk sdk) { this.sdk = sdk; }

    public async Task RunAsync()
    {
        Console.WriteLine("Ralen interactive mode. Type 'help' or 'exit'.");
        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
            if (line.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Commands: run [dir] [--project path] [--interactive]  |  create --lang X --name Y --dir D");
                continue;
            }
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var argv = parts; // reuse Program parsing by calling Main-like handler? for brevity call SDK directly:
            if (parts.Length > 0 && parts[0] == "run")
            {
                var path = parts.Length >= 2 ? parts[1] : Directory.GetCurrentDirectory();
                await sdk.RunAsync(path, null, true);
            }
            else if (parts.Length > 0 && (parts[0] == "create" || parts[0] == "make"))
            {
                string name = "NewApp", lang = "salang", version = "latest", dir = Directory.GetCurrentDirectory();
                for (int i = 1; i < parts.Length; i++)
                {
                    if (parts[i] == "--name" && i + 1 < parts.Length) name = parts[++i];
                    if (parts[i] == "--lang" && i + 1 < parts.Length) lang = parts[++i];
                    if (parts[i] == "--version" && i + 1 < parts.Length) version = parts[++i];
                    if (parts[i] == "--dir" && i + 1 < parts.Length) dir = parts[++i];
                }
                await sdk.CreateConsoleAsync(dir, name, lang, version);
            }
            else Console.WriteLine("Unknown command.");
        }
    }
}
