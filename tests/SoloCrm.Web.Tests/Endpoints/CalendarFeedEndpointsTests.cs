using SoloCrm.Web.Endpoints;

namespace SoloCrm.Web.Tests.Endpoints;

public sealed class CalendarFeedEndpointsTests
{
    [Fact]
    public void MaskPath_FeedPath_HidesToken()
    {
        var path = CalendarFeedEndpoints.FeedPath("0123456789abcdefghijklmnopqrstuvwxyzABCDEFG");

        CalendarFeedEndpoints.MaskPath(path).Should().Be("/calendar/{token}.ics");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/settings")]
    [InlineData("/api/v1/contacts")]
    public void MaskPath_OtherPath_IsUnchanged(string path)
    {
        CalendarFeedEndpoints.MaskPath(path).Should().Be(path);
    }
}
