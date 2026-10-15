using Serilog;
using Serilog.Core;
using Serilog.Events;
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

    [Fact]
    public void Filter_EventWithUnmaskedFeedPath_IsDropped()
    {
        var events = Log("/calendar/0123456789abcdefghijklmnopqrstuvwxyzABCDEFG.ics", "/calendar/{token}.ics", "/settings");

        events.Should().Equal("/calendar/{token}.ics", "/settings");
    }

    [Fact]
    public void Filter_EventWithoutRequestPath_IsKept()
    {
        var sink = new ListSink();
        using var logger = new LoggerConfiguration().Filter.ByExcluding(CalendarFeedEndpoints.ContainsToken).WriteTo.Sink(sink).CreateLogger();

        logger.Warning("No path here");

        sink.Paths.Should().ContainSingle();
    }

    private static List<string?> Log(params string[] paths)
    {
        var sink = new ListSink();
        using var logger = new LoggerConfiguration().Filter.ByExcluding(CalendarFeedEndpoints.ContainsToken).WriteTo.Sink(sink).CreateLogger();
        foreach (var path in paths)
        {
            logger.ForContext("RequestPath", path).Warning("Failed to determine the https port for redirect.");
        }

        return sink.Paths;
    }

    private sealed class ListSink : ILogEventSink
    {
        public List<string?> Paths { get; } = [];

        public void Emit(LogEvent logEvent) =>
            Paths.Add(logEvent.Properties.TryGetValue("RequestPath", out var v) && v is ScalarValue { Value: string s } ? s : null);
    }
}
