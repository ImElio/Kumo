using Kumo.Core.Updates;

namespace Kumo.Core.Tests.Updates;

public class InstallationSourceTests
{
    [Theory]
    [InlineData(true, true, """{"kind":"installer"}""", InstallationKind.Store)]
    [InlineData(true, false, null, InstallationKind.SideloadedPackage)]
    [InlineData(false, false, """{"kind":"installer"}""", InstallationKind.Installer)]
    [InlineData(false, false, """{"kind":"installer","version":"0.2.0"}""", InstallationKind.Installer)]
    [InlineData(false, false, null, InstallationKind.Portable)]
    [InlineData(false, false, "", InstallationKind.Portable)]
    [InlineData(false, false, """{"kind":"portable"}""", InstallationKind.Portable)]
    [InlineData(false, false, """{"kind":1}""", InstallationKind.Portable)]
    [InlineData(false, false, "not json", InstallationKind.Portable)]
    [InlineData(false, true, null, InstallationKind.Portable)]
    public void Installation_kind_comes_from_package_identity_and_the_installer_marker(bool packaged, bool store, string? marker, InstallationKind expected)
    {
        Assert.Equal(expected, InstallationSource.Resolve(packaged, store, marker));
    }
}
