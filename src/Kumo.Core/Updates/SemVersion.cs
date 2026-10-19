using System.Globalization;

namespace Kumo.Core.Updates;

/// <summary>
/// A Semantic Versioning 2.0.0 version with spec-compliant precedence. Build metadata is kept for display but never
/// affects comparison, so <c>1.0.0+abc</c> and <c>1.0.0+def</c> are equal in precedence.
/// </summary>
public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    private SemVersion(ulong major, ulong minor, ulong patch, IReadOnlyList<string> prerelease, string build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Build = build;
    }

    public ulong Major { get; }

    public ulong Minor { get; }

    public ulong Patch { get; }

    /// <summary>Dot-separated pre-release identifiers, empty for a stable version.</summary>
    public IReadOnlyList<string> Prerelease { get; }

    public string Build { get; }

    public bool IsPrerelease => Prerelease.Count > 0;

    /// <summary>The first pre-release identifier ("beta", "rc", ...), or empty for a stable version.</summary>
    public string Stage => IsPrerelease ? Prerelease[0] : string.Empty;

    public static SemVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"'{text}' is not a valid semantic version.");

    /// <summary>Strict SemVer 2.0.0 parsing: no leading "v", no leading zeros, no empty identifiers.</summary>
    public static bool TryParse(string? text, out SemVersion version)
    {
        version = null!;
        if (string.IsNullOrEmpty(text) || text.Length > 256)
            return false;

        string build = string.Empty;
        int plus = text.IndexOf('+');
        if (plus >= 0)
        {
            build = text[(plus + 1)..];
            text = text[..plus];
            if (!ValidIdentifiers(build, numericRules: false))
                return false;
        }

        IReadOnlyList<string> prerelease = [];
        int dash = text.IndexOf('-');
        if (dash >= 0)
        {
            string pre = text[(dash + 1)..];
            text = text[..dash];
            if (!ValidIdentifiers(pre, numericRules: true))
                return false;
            prerelease = pre.Split('.');
        }

        string[] core = text.Split('.');
        if (core.Length != 3 || !TryNumber(core[0], out ulong major) || !TryNumber(core[1], out ulong minor) || !TryNumber(core[2], out ulong patch))
            return false;

        version = new SemVersion(major, minor, patch, prerelease, build);
        return true;
    }

    /// <summary>Parses a release tag of the form <c>v1.2.3</c> (the leading "v" is required).</summary>
    public static bool TryParseTag(string? tag, out SemVersion version)
    {
        version = null!;
        return tag is { Length: > 1 } && tag[0] == 'v' && TryParse(tag[1..], out version);
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
            return 1;
        int result = Major.CompareTo(other.Major);
        if (result == 0)
            result = Minor.CompareTo(other.Minor);
        if (result == 0)
            result = Patch.CompareTo(other.Patch);
        if (result != 0)
            return result;

        if (!IsPrerelease || !other.IsPrerelease)
            return other.IsPrerelease.CompareTo(IsPrerelease);

        int count = Math.Min(Prerelease.Count, other.Prerelease.Count);
        for (int i = 0; i < count; i++)
        {
            result = CompareIdentifier(Prerelease[i], other.Prerelease[i]);
            if (result != 0)
                return result;
        }
        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public bool Equals(SemVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Prerelease));

    /// <summary>The version without build metadata.</summary>
    public string ToShortString() =>
        FormattableString.Invariant($"{Major}.{Minor}.{Patch}") + (IsPrerelease ? "-" + string.Join('.', Prerelease) : string.Empty);

    public override string ToString() => ToShortString() + (Build.Length > 0 ? "+" + Build : string.Empty);

    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;

    private static int CompareIdentifier(string left, string right)
    {
        bool leftNumeric = ulong.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out ulong l);
        bool rightNumeric = ulong.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out ulong r);
        if (leftNumeric && rightNumeric)
            return l.CompareTo(r);
        if (leftNumeric != rightNumeric)
            return leftNumeric ? -1 : 1;
        return string.CompareOrdinal(left, right);
    }

    private static bool TryNumber(string text, out ulong value)
    {
        value = 0;
        return text.Length > 0 && (text.Length == 1 || text[0] != '0') && text.All(char.IsAsciiDigit)
            && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool ValidIdentifiers(string text, bool numericRules)
    {
        if (text.Length == 0)
            return false;
        foreach (string identifier in text.Split('.'))
        {
            if (identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                return false;
            if (numericRules && identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsAsciiDigit))
                return false;
        }
        return true;
    }
}
