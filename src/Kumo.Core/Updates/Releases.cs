using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Kumo.Core.Updates;

[JsonConverter(typeof(UpdateChannelJsonConverter))]
public enum UpdateChannel
{
    Release,
    Beta,
    Development,
}

/// <summary>
/// Stores channels by name and reads the "Stable" name used by pre-release builds as Release, so an older settings
/// file is not mistaken for a corrupt one and reset.
/// </summary>
public sealed class UpdateChannelJsonConverter : JsonConverter<UpdateChannel>
{
    public override UpdateChannel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (string.Equals(name, "Stable", StringComparison.OrdinalIgnoreCase))
            return UpdateChannel.Release;
        return Enum.TryParse<UpdateChannel>(name, ignoreCase: true, out var channel) && Enum.IsDefined(channel) ? channel : UpdateChannel.Release;
    }

    public override void Write(Utf8JsonWriter writer, UpdateChannel value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>How this copy of Kumo was installed, which decides who owns its updates.</summary>
public enum InstallationKind
{
    /// <summary>Unpacked from a release ZIP; Kumo can report updates but the user replaces the files.</summary>
    Portable,

    /// <summary>Installed by Kumo's own per-user installer, identified by its installation marker.</summary>
    Installer,

    /// <summary>MSIX installed from the Microsoft Store; the Store owns updates.</summary>
    Store,

    /// <summary>MSIX installed outside the Store; replacing it with the GitHub installer would conflict.</summary>
    SideloadedPackage,
}

public enum Architecture
{
    X64,
    Arm64,
}

public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

public sealed record UpdateRelease(
    ReleasePlatform Platform,
    SemVersion Version,
    string Tag,
    string Title,
    string Notes,
    Uri PageUrl,
    DateTimeOffset? PublishedAt,
    bool IsDraft,
    bool IsPrerelease,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// Release asset naming convention shared with the release workflow (see docs/releases.md):
/// <c>Kumo-1.2.0-win-x64.zip</c>, <c>Kumo-1.2.0-win-x64-setup.exe</c> and <c>SHA256SUMS.txt</c>.
/// </summary>
public static partial class ReleaseAssets
{
    public const string Checksums = "SHA256SUMS.txt";

    public static string RuntimeId(Architecture architecture) => architecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    public static string PortableName(SemVersion version, Architecture architecture) => $"Kumo-{version.ToShortString()}-{RuntimeId(architecture)}.zip";

    public static string InstallerName(SemVersion version, Architecture architecture) => $"Kumo-{version.ToShortString()}-{RuntimeId(architecture)}-setup.exe";

    /// <summary>The asset this installation would use, or null when the release has none for it.</summary>
    public static ReleaseAsset? For(UpdateRelease release, InstallationKind kind, Architecture architecture)
    {
        string? name = kind switch
        {
            InstallationKind.Portable => PortableName(release.Version, architecture),
            InstallationKind.Installer => InstallerName(release.Version, architecture),
            _ => null,
        };
        return name == null ? null : release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
    }

    /// <summary>Asset names become local file names, so anything but this strict pattern is refused.</summary>
    public static bool IsSafeFileName(string name) => SafeName().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$")]
    private static partial Regex SafeName();
}

/// <summary>Channel rules: which published releases a given installation may be offered.</summary>
public static class ReleaseSelector
{
    /// <summary>Pre-release stages each channel accepts on top of stable releases. Unknown stages are never offered.</summary>
    public static IReadOnlySet<string> StagesFor(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => BetaStages,
        UpdateChannel.Development => DevelopmentStages,
        _ => NoStages,
    };

    private static readonly IReadOnlySet<string> NoStages = new HashSet<string>();
    private static readonly IReadOnlySet<string> BetaStages = new HashSet<string>(StringComparer.Ordinal) { "beta", "rc" };
    private static readonly IReadOnlySet<string> DevelopmentStages = new HashSet<string>(StringComparer.Ordinal) { "dev", "alpha", "beta", "rc" };

    public static bool IsEligible(UpdateRelease release, UpdateChannel channel)
    {
        if (release.IsDraft)
            return false;
        // GitHub's prerelease flag and the SemVer pre-release part must agree; a mismatch suggests a mistake in
        // publishing and the release is ignored rather than guessed at.
        if (release.IsPrerelease != release.Version.IsPrerelease)
            return false;
        if (!release.Version.IsPrerelease)
            return true;
        return StagesFor(channel).Contains(release.Version.Stage);
    }

    /// <summary>
    /// The highest eligible version of this platform above <paramref name="current"/> that has an asset for this
    /// installation, chosen by SemVer precedence rather than publication date or GitHub's "latest" flag. Never returns
    /// an older or equal version.
    /// </summary>
    public static UpdateRelease? SelectUpdate(IEnumerable<UpdateRelease> releases, ReleasePlatform platform, SemVersion current,
        UpdateChannel channel, InstallationKind kind, Architecture architecture) =>
        releases
            .Where(r => r.Platform == platform && IsEligible(r, channel) && r.Version > current && ReleaseAssets.For(r, kind, architecture) != null)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();
}

public static class InstallerPolicy
{
    /// <summary>
    /// A downloaded installer may run only if Windows trusts its signature, the signer is exactly the publisher this
    /// build expects, and the file declares the version that was offered. Any missing fact means "no".
    /// </summary>
    public static bool Accept(bool signatureTrusted, string? signerSubject, string expectedPublisher, string? productVersion, SemVersion offered) =>
        signatureTrusted
        && expectedPublisher.Length > 0
        && string.Equals(signerSubject, expectedPublisher, StringComparison.Ordinal)
        && SemVersion.TryParse(productVersion, out var fileVersion)
        && fileVersion.Equals(offered);

    /// <summary>
    /// For builds without a configured publisher: the installer already matched the release's SHA-256 list, and must
    /// also declare exactly the offered version, so a renamed older installer is never run as an update.
    /// </summary>
    public static bool AcceptChecked(string? productVersion, SemVersion offered) =>
        SemVersion.TryParse(productVersion, out var fileVersion) && fileVersion.Equals(offered);
}
