using Kumo.Core.Updates;

namespace Kumo.Core.Tests.Updates;

public class InstallationSourceTests
{
    [Theory]
    [InlineData(true, true, """{"kind":"installer"}""", InstallationKind.Store)]
    [InlineData(true, false, null, InstallationKind.SideloadedPackage)]
    [InlineData(false, false, """{"kind":"installer"}""", InstallationKind.Installer)]
    [InlineData(false, false, """{"kind":"installer","version":"0.2.0"}""", InstallationKind.Installer)]
    [InlineData(false, false, null, InstallationKind.NotInstalled)]
    [InlineData(false, false, "", InstallationKind.NotInstalled)]
    [InlineData(false, false, """{"kind":"portable"}""", InstallationKind.NotInstalled)]
    [InlineData(false, false, """{"kind":1}""", InstallationKind.NotInstalled)]
    [InlineData(false, false, "not json", InstallationKind.NotInstalled)]
    [InlineData(false, true, null, InstallationKind.NotInstalled)]
    public void Installation_kind_comes_from_package_identity_and_the_installer_marker(bool packaged, bool store, string? marker, InstallationKind expected)
    {
        Assert.Equal(expected, InstallationSource.Resolve(packaged, store, marker));
    }

    [Theory]
    [InlineData(InstallationKind.Store, "Microsoft Store")]
    [InlineData(InstallationKind.Installer, "GitHub")]
    [InlineData(InstallationKind.NotInstalled, "Not installed (development build)")]
    [InlineData(InstallationKind.SideloadedPackage, "Unknown")]
    public void Installation_source_is_named_for_people(InstallationKind kind, string expected)
    {
        Assert.Equal(expected, InstallationSource.Describe(kind));
    }
}
