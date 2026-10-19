using Kumo.Core.Updates;

namespace Kumo.Core.Tests.Updates;

public class InstallerPolicyTests
{
    private const string Publisher = "CN=Kumo Publisher, O=Kumo, C=IT";
    private static readonly SemVersion Offered = SemVersion.Parse("0.2.0");

    [Fact]
    public void A_trusted_installer_from_the_expected_publisher_with_the_offered_version_is_accepted()
    {
        Assert.True(InstallerPolicy.Accept(true, Publisher, Publisher, "0.2.0+abc123", Offered));
    }

    [Theory]
    [InlineData(false, Publisher, Publisher, "0.2.0")]
    [InlineData(true, "CN=Someone Else", Publisher, "0.2.0")]
    [InlineData(true, "cn=kumo publisher, o=kumo, c=it", Publisher, "0.2.0")]
    [InlineData(true, null, Publisher, "0.2.0")]
    [InlineData(true, Publisher, "", "0.2.0")]
    [InlineData(true, Publisher, Publisher, "0.1.9")]
    [InlineData(true, Publisher, Publisher, "0.2.0-beta.1")]
    [InlineData(true, Publisher, Publisher, null)]
    [InlineData(true, Publisher, Publisher, "garbage")]
    public void Anything_less_is_rejected(bool trusted, string? subject, string expected, string? productVersion)
    {
        Assert.False(InstallerPolicy.Accept(trusted, subject, expected, productVersion, Offered));
    }
}
