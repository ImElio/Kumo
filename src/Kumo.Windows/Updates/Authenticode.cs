using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Kumo.Windows.Updates;

/// <param name="IsTrusted">WinVerifyTrust accepted the signature, its chain and revocation status.</param>
/// <param name="Subject">Subject of the signing certificate, when one could be read.</param>
/// <param name="Status">The raw WinVerifyTrust result, for the log.</param>
public sealed record SignatureCheck(bool IsTrusted, string? Subject, int Status)
{
    public bool IsSignedBy(string expectedSubject) =>
        IsTrusted && expectedSubject.Length > 0 && string.Equals(Subject, expectedSubject, StringComparison.Ordinal);
}

/// <summary>Verifies an embedded Authenticode signature with the same policy Windows uses for downloaded programs.</summary>
public static unsafe partial class Authenticode
{
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT = 0x80;
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public char* pcwszFilePath;
        public nint hFile;
        public nint pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public WINTRUST_FILE_INFO* pFile;
        public uint dwStateAction;
        public nint hWVTStateData;
        public nint pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public nint pSignatureSettings;
    }

    /// <remarks>
    /// Revocation is checked for the whole chain, so an offline machine cannot verify an installer and the update is
    /// refused rather than installed unchecked. Catalog-signed system files are not covered; Kumo installers carry
    /// an embedded signature.
    /// </remarks>
    public static SignatureCheck Verify(string path)
    {
        string fullPath = Path.GetFullPath(path);
        int status;
        fixed (char* file = fullPath)
        {
            var fileInfo = new WINTRUST_FILE_INFO { cbStruct = (uint)sizeof(WINTRUST_FILE_INFO), pcwszFilePath = file };
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = &fileInfo,
                dwStateAction = WTD_STATEACTION_IGNORE,
                dwProvFlags = WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT,
            };
            var action = GenericVerifyV2;
            status = WinVerifyTrust(-1, &action, &data);
        }
        return new SignatureCheck(status == 0, status == 0 ? SignerSubject(fullPath) : null, status);
    }

    private static string? SignerSubject(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // No X509CertificateLoader equivalent exists for reading the signer of a signed file.
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return certificate.Subject;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(nint hwnd, Guid* pgActionID, WINTRUST_DATA* pWVTData);
}
