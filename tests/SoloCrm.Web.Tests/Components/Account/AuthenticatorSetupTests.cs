using SoloCrm.Web.Components.Account;

namespace SoloCrm.Web.Tests.Components.Account;

public sealed class AuthenticatorSetupTests
{
    [Theory]
    [InlineData("ABCDEFGHIJKLMNOP", "abcd efgh ijkl mnop")]
    [InlineData("ABCDEF", "abcd ef")]
    [InlineData("", "")]
    public void FormatKey_Key_GroupsOfFourInLowerCase(string key, string expected) =>
        AuthenticatorSetup.FormatKey(key).Should().Be(expected);

    [Fact]
    public void Uri_EmailAndKey_FollowsKeyUriFormatWithSoloCrmAsIssuer() =>
        AuthenticatorSetup.Uri("ada+crm@example.test", "JBSWY3DPEHPK3PXP").Should()
            .Be("otpauth://totp/SoloCRM:ada%2Bcrm%40example.test?secret=JBSWY3DPEHPK3PXP&issuer=SoloCRM&digits=6");

    [Fact]
    public void QrCodeSvg_Uri_ReturnsScalableSvg()
    {
        var svg = AuthenticatorSetup.QrCodeSvg("otpauth://totp/SoloCRM:ada%40example.test?secret=JBSWY3DPEHPK3PXP&issuer=SoloCRM&digits=6");

        svg.Should().StartWith("<svg").And.Contain("viewBox").And.EndWith("</svg>");
    }
}
