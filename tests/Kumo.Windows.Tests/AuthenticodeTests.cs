using Kumo.Windows.Updates;

namespace Kumo.Windows.Tests;

public sealed class AuthenticodeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kumo-sign-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string SignedSystemBinary()
    {
        string dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        Assert.True(File.Exists(dotnet), "The signed fixture dotnet.exe is not installed in Program Files.");
        return dotnet;
    }

    [Fact]
    public void Unsigned_files_are_not_trusted()
    {
        string path = Path.Combine(_directory, "Kumo-9.9.9-win-x64-setup.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, .. new byte[4096]]);

        var check = Authenticode.Verify(path);

        Assert.False(check.IsTrusted);
        Assert.False(check.IsSignedBy("CN=Kumo"));
    }

    [Fact]
    public void Tampered_signed_files_are_not_trusted()
    {
        string copy = Path.Combine(_directory, "tampered.exe");
        File.Copy(SignedSystemBinary(), copy);
        var bytes = File.ReadAllBytes(copy);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(copy, bytes);

        Assert.False(Authenticode.Verify(copy).IsTrusted);
    }

    [Fact]
    public void Validly_signed_files_from_another_publisher_are_rejected()
    {
        var check = Authenticode.Verify(SignedSystemBinary());

        Assert.True(check.IsTrusted, $"WinVerifyTrust returned 0x{check.Status:X8}");
        Assert.Contains("Microsoft Corporation", check.Subject);
        Assert.True(check.IsSignedBy(check.Subject!));
        Assert.False(check.IsSignedBy("CN=Kumo Publisher"));
        Assert.False(check.IsSignedBy(string.Empty));
    }

    [Fact]
    public void The_test_host_is_not_packaged()
    {
        Assert.False(PackageIdentity.IsPackaged());
    }
}
