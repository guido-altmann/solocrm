using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.CalendarFeeds;
using SoloCrm.Application.Features.Common;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>
/// Renders a task as <c>VTODO</c> for the task list „SoloCRM“ (ADR-014) and reads the status of a copy that was
/// changed in Nextcloud. The output is deterministic: <c>DTSTAMP</c> is the last change of the task, not the render
/// time, because the sync detects changes by the hash of the payload.
/// </summary>
public static class TaskVTodo
{
    public const string MediaType = "text/calendar; charset=utf-8";

    /// <summary>Completed tasks stay in the list for this many days, then they are removed there (as in the ICS feed).</summary>
    public const int CompletedRetentionDays = GetTaskFeed.CompletedRetentionDays;

    public sealed record Item(
        Guid Id,
        string Title,
        DateOnly? DueDate,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? ContactId,
        Guid? OrganizationId,
        Guid? OpportunityId);

    public static string ResourceName(Guid id) => $"{id}.ics";

    /// <param name="baseUrl">Origin of the app for the link to the linked record; without it the task has no link.</param>
    public static DavContent Render(Item task, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(task);

        var todo = new Todo
        {
            Uid = ContactVCard.Uid(task.Id),
            Summary = task.Title,
            DtStamp = ToUtc(task.UpdatedAt),
            Created = ToUtc(task.CreatedAt),
            LastModified = ToUtc(task.UpdatedAt),
            Status = task.CompletedAt is null ? "NEEDS-ACTION" : "COMPLETED",
        };

        if (task.DueDate is { } due)
        {
            todo.Due = new CalDateTime(due.Year, due.Month, due.Day);
        }

        if (task.CompletedAt is { } completedAt)
        {
            todo.Completed = ToUtc(completedAt);
            todo.PercentComplete = 100;
        }

        if (RecordLinks.ForTask(baseUrl, task.OpportunityId, task.ContactId, task.OrganizationId) is { } link)
        {
            todo.Url = link;
            todo.Description = link.AbsoluteUri;
        }

        var calendar = new Calendar();
        calendar.Todos.Add(todo);
        return new DavContent(new CalendarSerializer().SerializeToString(calendar) ?? string.Empty, MediaType);
    }

    /// <summary>
    /// Whether the <c>VTODO</c> in <paramref name="icalendar"/> is done: <c>true</c> for <c>STATUS:COMPLETED</c> (or a
    /// <c>COMPLETED</c> date without status), <c>false</c> for an open task, <c>null</c> if unreadable or cancelled.
    /// </summary>
    public static bool? IsCompleted(string icalendar)
    {
        Todo? todo;
        try
        {
            todo = Calendar.Load(icalendar)?.Todos.FirstOrDefault();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or System.Runtime.Serialization.SerializationException)
        {
            return null;
        }

        return todo?.Status?.ToUpperInvariant() switch
        {
            null => todo is null ? null : todo.Completed is not null,
            "COMPLETED" => true,
            "NEEDS-ACTION" or "IN-PROCESS" => false,
            _ => null,
        };
    }

    private static CalDateTime ToUtc(DateTimeOffset value) => new(value.UtcDateTime, "UTC");
}
