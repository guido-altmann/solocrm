using SoloCrm.Domain.Organizations;

namespace SoloCrm.Domain.Tests.Organizations;

public sealed class WebsiteTests
{
    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData(" Example.COM/ ", "https://example.com")]
    [InlineData("HTTP://www.Example.com", "http://www.example.com")]
    [InlineData("https://example.com:443/", "https://example.com")]
    [InlineData("https://example.com:8443/team/", "https://example.com:8443/team")]
    [InlineData("example.com/jobs?id=4#top", "https://example.com/jobs?id=4")]
    public void TryNormalize_ValidInput_ReturnsNormalizedUrl(string input, string expected)
    {
        Website.TryNormalize(input, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("not a url")]
    [InlineData("localhost")]
    [InlineData("ftp://example.com")]
    [InlineData("https://192.168.1.1")]
    [InlineData("https://user:secret@example.com")]
    public void TryNormalize_InvalidInput_ReturnsFalse(string? input)
    {
        Website.TryNormalize(input, out _).Should().BeFalse();
    }
}
