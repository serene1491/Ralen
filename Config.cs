using System.Text.Json;
namespace Ralen;

public class Config
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
                File.WriteAllText(cfgPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
                return cfg;
            }
            var raw = File.ReadAllText(cfgPath);
            var obj = JsonSerializer.Deserialize<Config>(raw) ?? new Config();
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
        File.WriteAllText(cfgPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}