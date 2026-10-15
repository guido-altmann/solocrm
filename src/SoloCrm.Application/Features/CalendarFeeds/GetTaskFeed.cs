using FluentValidation;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.CalendarFeeds;

/// <summary>
/// Renders the tasks as an iCalendar feed (read-only, SOL-6): all open tasks with a due date plus the ones completed
/// in the last <see cref="CompletedRetentionDays"/> days, so that calendar apps show recent completions. Each task
/// becomes an all-day <c>VEVENT</c> on its due date, because Apple and Google Calendar ignore <c>VTODO</c> in
/// subscribed calendars. Tasks without a due date have no place in a calendar and are left out.
/// The token from the feed URL is the only authentication; it is checked against the stored hash.
/// </summary>
public static class GetTaskFeed
{
    public const int CompletedRetentionDays = 30;

    public const string CalendarName = "SoloCRM Aufgaben";

    /// <summary>Prefix of the title of completed tasks.</summary>
    public const string CompletedMark = "✓";

    /// <param name="Token">Secret from the feed URL.</param>
    /// <param name="BaseUrl">Origin of the app without trailing slash, used for the links to the linked records.</param>
    public sealed record Query(string? Token, string? BaseUrl);

    public sealed record Result(string Content);

    public sealed class Validator : AbstractValidator<Query>;

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Query> validator, TimeProvider timeProvider)
        : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(query, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            if (!CalendarFeedToken.IsWellFormed(query.Token))
            {
                return CalendarFeedErrors.Invalid;
            }

            var now = timeProvider.GetUtcNow();
            var hash = CalendarFeedToken.Hash(query.Token!);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var feed = await db.CalendarFeeds.SingleOrDefaultAsync(f => f.TokenHash == hash, cancellationToken);
            if (feed is null)
            {
                return CalendarFeedErrors.Invalid;
            }

            if (feed.RecordUse(now))
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            var cutoff = now.AddDays(-CompletedRetentionDays);
            var tasks = await db.Tasks.AsNoTracking()
                .Where(t => t.DueDate != null && (t.CompletedAt == null || t.CompletedAt >= cutoff))
                .OrderBy(t => t.DueDate)
                .ThenBy(t => t.Id)
                .Select(t => new FeedTask(t.Id, t.Title, t.DueDate, t.CompletedAt, t.CreatedAt, t.UpdatedAt, t.ContactId, t.OrganizationId, t.OpportunityId))
                .ToListAsync(cancellationToken);

            return new Result(Render(tasks, query.BaseUrl, now));
        }
    }

    private sealed record FeedTask(
        Guid Id,
        string Title,
        DateOnly? DueDate,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? ContactId,
        Guid? OrganizationId,
        Guid? OpportunityId);

    private static string Render(List<FeedTask> tasks, string? baseUrl, DateTimeOffset now)
    {
        var calendar = new Calendar();
        calendar.AddProperty("X-WR-CALNAME", CalendarName);
        calendar.AddProperty("REFRESH-INTERVAL;VALUE=DURATION", "PT1H");
        calendar.AddProperty("X-PUBLISHED-TTL", "PT1H");

        foreach (var task in tasks)
        {
            var due = task.DueDate!.Value;
            var next = due.AddDays(1);
            var calendarEvent = new CalendarEvent
            {
                Uid = $"{task.Id}@solocrm",
                Summary = task.CompletedAt is null ? task.Title : $"{CompletedMark} {task.Title}",
                DtStamp = ToUtc(now),
                Created = ToUtc(task.CreatedAt),
                LastModified = ToUtc(task.UpdatedAt),
                DtStart = new CalDateTime(due.Year, due.Month, due.Day),
                DtEnd = new CalDateTime(next.Year, next.Month, next.Day),
                Transparency = "TRANSPARENT",
            };

            if (LinkFor(task, baseUrl) is { } link)
            {
                calendarEvent.Url = link;
                calendarEvent.Description = link.AbsoluteUri;
            }

            calendar.Events.Add(calendarEvent);
        }

        return new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
    }

    private static CalDateTime ToUtc(DateTimeOffset value) => new(value.UtcDateTime, "UTC");

    /// <summary>Link to the most specific linked record: opportunity, then contact, then organization.</summary>
    private static Uri? LinkFor(FeedTask task, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var path = (task.OpportunityId, task.ContactId, task.OrganizationId) switch
        {
            ({ } id, _, _) => $"/opportunities/{id}",
            (_, { } id, _) => $"/contacts/{id}",
            (_, _, { } id) => $"/organizations/{id}",
            _ => null,
        };

        return path is not null && Uri.TryCreate(baseUrl.TrimEnd('/') + path, UriKind.Absolute, out var uri) ? uri : null;
    }
}
