namespace Kumo.Core.Updates;

/// <summary>An operating system Kumo is released for. Each platform has its own tags and its own version line.</summary>
public enum ReleasePlatform
{
    Windows,
    Linux,
}

/// <summary>
/// Release tags carry their platform: <c>windows/v1.2.0</c>, <c>linux/v0.1.0-beta.1</c>. The versions of different
/// platforms are unrelated, so a release is only ever compared with releases of the same platform.
/// </summary>
public static class ReleaseTag
{
    public static string Prefix(ReleasePlatform platform) => platform switch
    {
        ReleasePlatform.Windows => "windows/",
        ReleasePlatform.Linux => "linux/",
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };

    public static string Format(ReleasePlatform platform, SemVersion version) => Prefix(platform) + "v" + version;

    /// <summary>
    /// Reads a platform tag. A bare <c>vX.Y.Z</c> tag predates platform prefixes and only ever meant Windows, so it is
    /// read as a Windows release; any other prefix is unknown and refused.
    /// </summary>
    public static bool TryParse(string? tag, out ReleasePlatform platform, out SemVersion version)
    {
        platform = ReleasePlatform.Windows;
        version = null!;
        if (tag == null)
            return false;
        foreach (var candidate in Enum.GetValues<ReleasePlatform>())
        {
            string prefix = Prefix(candidate);
            if (tag.StartsWith(prefix, StringComparison.Ordinal))
            {
                platform = candidate;
                return SemVersion.TryParseTag(tag[prefix.Length..], out version);
            }
        }
        return SemVersion.TryParseTag(tag, out version);
    }

    /// <summary>
    /// The tag of the highest release of <paramref name="platform"/> that precedes <paramref name="version"/> by SemVer
    /// precedence, used as the start of generated release notes. Other platforms and unparsable tags are ignored.
    /// </summary>
    public static string? FindPrevious(ReleasePlatform platform, SemVersion version, IEnumerable<string> tags)
    {
        string? previous = null;
        SemVersion? best = null;
        foreach (string tag in tags)
        {
            if (TryParse(tag.Trim(), out var tagPlatform, out var tagVersion) && tagPlatform == platform
                && tagVersion < version && (best == null || tagVersion > best))
            {
                best = tagVersion;
                previous = tag.Trim();
            }
        }
        return previous;
    }
}
