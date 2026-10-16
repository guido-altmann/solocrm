using SoloCrm.Application.Features.Nextcloud;

namespace SoloCrm.Application.Tests.Features.Nextcloud;

public sealed class TaskVTodoTests
{
    private const string BaseUrl = "https://crm.example.test/";

    private static readonly Guid Id = Guid.Parse("0199f0a0-0000-7000-8000-000000000002");
    private static readonly Guid ContactId = Guid.Parse("0199f0a0-0000-7000-8000-000000000003");
    private static readonly Guid OpportunityId = Guid.Parse("0199f0a0-0000-7000-8000-000000000004");
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2026, 10, 2, 9, 30, 0, TimeSpan.FromHours(2));

    private static TaskVTodo.Item Item(DateOnly? due = null, DateTimeOffset? completedAt = null, Guid? contactId = null, Guid? opportunityId = null) =>
        new(Id, "Angebot nachfassen", due, completedAt, Created, Updated, contactId, null, opportunityId);

    // Unfolds long lines (RFC 5545 3.1) before comparing.
    private static string[] Lines(TaskVTodo.Item item, string? baseUrl = BaseUrl) =>
        TaskVTodo.Render(item, baseUrl).Body.Replace("\r\n ", "", StringComparison.Ordinal).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Render_OpenTaskWithDueDate_WritesAllDayDueAndNeedsAction()
    {
        var lines = Lines(Item(due: new DateOnly(2026, 10, 20), contactId: ContactId));

        lines.Should().Contain([
            "BEGIN:VTODO",
            $"UID:{Id}@solocrm",
            "SUMMARY:Angebot nachfassen",
            "DUE;VALUE=DATE:20261020",
            "STATUS:NEEDS-ACTION",
            "DTSTAMP:20261002T073000Z",
            "LAST-MODIFIED:20261002T073000Z",
            "CREATED:20261001T080000Z",
            $"URL:https://crm.example.test/contacts/{ContactId}",
            $"DESCRIPTION:https://crm.example.test/contacts/{ContactId}",
        ]);
        lines.Should().NotContain(l => l.StartsWith("COMPLETED", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_CompletedTask_WritesCompletedStatusAndTime()
    {
        var lines = Lines(Item(completedAt: new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(2))));

        lines.Should().Contain(["STATUS:COMPLETED", "COMPLETED:20261003T100000Z", "PERCENT-COMPLETE:100"]);
    }

    [Fact]
    public void Render_LinkedToContactAndOpportunity_LinksTheOpportunity()
    {
        var lines = Lines(Item(contactId: ContactId, opportunityId: OpportunityId));

        lines.Should().Contain($"URL:https://crm.example.test/opportunities/{OpportunityId}");
    }

    [Fact]
    public void Render_WithoutDueDateOrBaseUrl_LeavesDueAndLinkOut()
    {
        var lines = Lines(Item(contactId: ContactId), baseUrl: null);

        lines.Should().NotContain(l => l.StartsWith("DUE", StringComparison.Ordinal)
            || l.StartsWith("URL", StringComparison.Ordinal)
            || l.StartsWith("DESCRIPTION", StringComparison.Ordinal));
        TaskVTodo.Render(Item(), BaseUrl).MediaType.Should().Be("text/calendar; charset=utf-8");
    }

    [Fact]
    public void Render_SameTaskTwice_IsByteIdentical()
    {
        var first = TaskVTodo.Render(Item(due: new DateOnly(2026, 10, 20)), BaseUrl).Body;

        TaskVTodo.Render(Item(due: new DateOnly(2026, 10, 20)), BaseUrl).Body.Should().Be(first);
    }

    [Theory]
    [InlineData("STATUS:COMPLETED\r\nCOMPLETED:20261016T100000Z\r\nPERCENT-COMPLETE:100", true)]
    [InlineData("STATUS:NEEDS-ACTION", false)]
    [InlineData("STATUS:IN-PROCESS\r\nPERCENT-COMPLETE:50", false)]
    [InlineData("COMPLETED:20261016T100000Z", true)]
    [InlineData("", false)]
    [InlineData("STATUS:CANCELLED", null)]
    public void IsCompleted_RemoteTodo_ReadsTheStatus(string properties, bool? expected)
    {
        var ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Nextcloud Tasks//EN\r\nBEGIN:VTODO\r\nUID:x@solocrm\r\nDTSTAMP:20261016T100000Z\r\nSUMMARY:Test\r\n"
            + (properties.Length > 0 ? properties + "\r\n" : "")
            + "END:VTODO\r\nEND:VCALENDAR\r\n";

        TaskVTodo.IsCompleted(ics).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein iCalendar")]
    [InlineData("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n")]
    public void IsCompleted_Unreadable_ReturnsNull(string ics)
    {
        TaskVTodo.IsCompleted(ics).Should().BeNull();
    }

    [Fact]
    public void IsCompleted_OwnRendering_RoundTrips()
    {
        TaskVTodo.IsCompleted(TaskVTodo.Render(Item(completedAt: Created), BaseUrl).Body).Should().BeTrue();
        TaskVTodo.IsCompleted(TaskVTodo.Render(Item(), BaseUrl).Body).Should().BeFalse();
    }
}
