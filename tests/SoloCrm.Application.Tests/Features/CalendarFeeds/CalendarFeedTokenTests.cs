using SoloCrm.Application.Features.CalendarFeeds;

namespace SoloCrm.Application.Tests.Features.CalendarFeeds;

public sealed class CalendarFeedTokenTests
{
    [Fact]
    public void Generate_Always_ReturnsWellFormedUniqueTokenWithMatchingHash()
    {
        var first = CalendarFeedToken.Generate();
        var second = CalendarFeedToken.Generate();

        first.Token.Should().HaveLength(CalendarFeedToken.Length);
        CalendarFeedToken.IsWellFormed(first.Token).Should().BeTrue();
        first.Token.Should().NotBe(second.Token);
        first.Hash.Should().Be(CalendarFeedToken.Hash(first.Token)).And.HaveLength(64);
        first.Hash.Should().NotContain(first.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("0123456789012345678901234567890123456789012!")]
    [InlineData("01234567890123456789012345678901234567890123")]
    public void IsWellFormed_WrongShape_ReturnsFalse(string? token)
    {
        CalendarFeedToken.IsWellFormed(token).Should().BeFalse();
    }
}
