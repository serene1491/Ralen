namespace Ralen;

// small semver helper that tolerates non-semver strings
public class VersionOrFallback : IComparable<VersionOrFallback>
{
    readonly string raw;
    public VersionOrFallback(string s) => raw = s;
    public int CompareTo(VersionOrFallback other)
    {
        if (other == null) return 1;
        if (Version.TryParse(raw, out var v1) && Version.TryParse(other.raw, out var v2))
            return v1.CompareTo(v2);
        return string.Compare(raw, other.raw, StringComparison.OrdinalIgnoreCase);
    }
    public override string ToString() => raw;
}
