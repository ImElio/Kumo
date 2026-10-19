using System.Text.Json;

namespace Kumo.Core.Updates;

/// <summary>
/// Decides who owns updates for this copy of Kumo from facts the installation itself establishes: MSIX package
/// identity (set by Windows) and the marker file written by Kumo's installer. Folder names are never consulted.
/// </summary>
public static class InstallationSource
{
    /// <summary>Written next to Kumo.exe by Kumo's own installer and removed by its uninstaller.</summary>
    public const string MarkerFileName = "Kumo.installation.json";

    public static InstallationKind Resolve(bool packaged, bool storeSigned, string? markerJson)
    {
        if (packaged)
            return storeSigned ? InstallationKind.Store : InstallationKind.SideloadedPackage;
        return IsInstallerMarker(markerJson) ? InstallationKind.Installer : InstallationKind.Portable;
    }

    /// <summary>The marker must be exactly <c>{"kind":"installer"}</c> (extra properties allowed); anything else means portable.</summary>
    public static bool IsInstallerMarker(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4096)
            return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == "installer";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Describe(InstallationKind kind) => kind switch
    {
        InstallationKind.Store => "Microsoft Store",
        InstallationKind.SideloadedPackage => "MSIX package (outside the Store)",
        InstallationKind.Installer => "Kumo installer",
        _ => "Portable",
    };
}
