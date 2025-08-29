namespace Ralen;

public class LocalInstaller
{
    readonly Config cfg;
    readonly UserPathConfigurator pathConfigurator;

    public LocalInstaller(Config cfg)
    {
        this.cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        pathConfigurator = new UserPathConfigurator(cfg);
    }

    public void ConfigurePathNow(bool addToPath = true, bool createShims = true)
        => pathConfigurator.ConfigurePathNow(addToPath, createShims);

    public Task AddPackageAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
        var modulesDir = Path.Combine(cfg.InstallDir, "modules");
        Directory.CreateDirectory(modulesDir);

        var name = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(name)) name = $"package_{Guid.NewGuid():N}";

        var dest = Path.Combine(modulesDir, name);
        if (File.Exists(dest))
        {
            Console.WriteLine($"Package already exists: {dest}");
            return Task.CompletedTask;
        }

        // We expect caller to use RemoteInstaller.DownloadToFileAsync for actual download.
        throw new InvalidOperationException("LocalInstaller.AddPackageAsync should be used in conjunction with RemoteInstaller to download the file. Use SimpleInstaller.AddPackageAsync instead.");
    }
}
