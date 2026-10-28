using SoloCrm.Web;

namespace SoloCrm.Web.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.11.1-5+Branch.main.Sha.63cfec9", "1.11.1-5")]
    [InlineData("1.12.0", "1.12.0")]
    public void Parse_VersionWithMetadata_ReturnsVersionWithoutMetadata(string input, string expected) =>
        AppVersion.Parse(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_MissingVersion_ReturnsDev(string? input) =>
        AppVersion.Parse(input).Should().Be("dev");

    [Fact]
    public void Current_BuiltWithGitVersion_IsNotEmpty() =>
        AppVersion.Current.Should().NotBeNullOrWhiteSpace();
}
