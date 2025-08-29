namespace Ralen;

public interface IInstaller
{
    Task<string> EnsureInstalledAsync(string lang, string versionSpecifier, string ownerRepoOverride = null);
    Task<string> GetLatestRemoteVersionAsync(string lang, string ownerRepoOverride = null);
    Task<bool> VerifyChecksumAsync(string lang, string version);
    Task RunSmokeTestAsync(string lang, string runtimePath);
    Task AddPackageAsync(string url);
    void ConfigurePathNow(bool addToPath = true, bool createShims = true);
}
