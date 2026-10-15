using FluentValidation;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.CalendarFeeds;

/// <summary>
/// Renders the tasks as an iCalendar feed (<c>VTODO</c>, read-only, SOL-6): all open tasks plus the ones completed in
/// the last <see cref="CompletedRetentionDays"/> days, so that calendar apps show recent completions correctly.
/// The token from the feed URL is the only authentication; it is checked against the stored hash.
/// </summary>
public static class GetTaskFeed
{
    public const int CompletedRetentionDays = 30;

    public const string CalendarName = "SoloCRM Aufgaben";

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
                .Where(t => t.CompletedAt == null || t.CompletedAt >= cutoff)
                .OrderBy(t => t.DueDate == null)
                .ThenBy(t => t.DueDate)
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
            var todo = new Todo
            {
                Uid = $"{task.Id}@solocrm",
                Summary = task.Title,
                DtStamp = ToUtc(now),
                Created = ToUtc(task.CreatedAt),
                LastModified = ToUtc(task.UpdatedAt),
                Status = task.CompletedAt is null ? TodoStatus.NeedsAction : TodoStatus.Completed,
            };

            if (task.DueDate is { } due)
            {
                todo.Due = new CalDateTime(due.Year, due.Month, due.Day);
            }

            if (task.CompletedAt is { } completed)
            {
                todo.Completed = ToUtc(completed);
            }

            if (LinkFor(task, baseUrl) is { } link)
            {
                todo.Url = link;
                todo.Description = link.AbsoluteUri;
            }

            calendar.Todos.Add(todo);
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
