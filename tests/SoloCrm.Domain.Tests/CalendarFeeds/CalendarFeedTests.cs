using SoloCrm.Domain.CalendarFeeds;

namespace SoloCrm.Domain.Tests.CalendarFeeds;

public sealed class CalendarFeedTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ValidHash_IsUnused()
    {
        var feed = CalendarFeed.Create("hash");

        feed.TokenHash.Should().Be("hash");
        feed.LastUsedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankHash_Throws(string hash)
    {
        var act = () => CalendarFeed.Create(hash);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordUse_WithinPrecision_KeepsStoredValue()
    {
        var feed = CalendarFeed.Create("hash");
        feed.RecordUse(Now).Should().BeTrue();

        feed.RecordUse(Now.AddSeconds(30)).Should().BeFalse();

        feed.LastUsedAt.Should().Be(Now);
    }

    [Fact]
    public void RecordUse_AfterPrecision_UpdatesValue()
    {
        var feed = CalendarFeed.Create("hash");
        feed.RecordUse(Now);

        feed.RecordUse(Now.AddMinutes(1)).Should().BeTrue();

        feed.LastUsedAt.Should().Be(Now.AddMinutes(1));
    }
}
