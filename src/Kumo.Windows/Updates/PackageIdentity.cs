using System.Runtime.InteropServices;

namespace Kumo.Windows.Updates;

public static partial class PackageIdentity
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>True when the process runs with MSIX package identity, which is decided by the OS, not by paths.</summary>
    public static bool IsPackaged()
    {
        uint length = 0;
        int result = GetCurrentPackageFullName(ref length, 0);
        return result != APPMODEL_ERROR_NO_PACKAGE && (result == ERROR_INSUFFICIENT_BUFFER || result == 0);
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);
}
