using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Today;

/// <summary>
/// The „Heute“ view (US-12, SPEC 3.3 S1): overdue tasks, tasks due today, stale requests and recently edited
/// records, plus the tasks without due date (collapsed, iteration 3 decision 4). „Today“ is determined in the
/// configured time zone (decision 5).
/// </summary>
public static class GetToday
{
    public const int RecentlyEditedLimit = 10;

    public sealed record Query;

    /// <summary>An open request without activity for <see cref="DaysSinceActivity"/> days (at least the threshold).</summary>
    public sealed record StaleOpportunity(
        Guid Id,
        string Title,
        string StageName,
        string? ClientName,
        DateTimeOffset LastActivityAt,
        int DaysSinceActivity);

    public sealed record RecentRecord(TimelineRecordType Type, Guid Id, string Name, DateTimeOffset UpdatedAt);

    public sealed record Result(
        DateOnly Today,
        int StaleOpportunityDays,
        IReadOnlyList<TaskSummary> Overdue,
        IReadOnlyList<TaskSummary> DueToday,
        IReadOnlyList<TaskSummary> WithoutDueDate,
        IReadOnlyList<StaleOpportunity> StaleOpportunities,
        IReadOnlyList<RecentRecord> RecentlyEdited);

    public sealed class Handler(ICrmDbContextFactory dbFactory, IAppSettings settings, AppClock clock) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var today = clock.Today;
            var staleDays = await settings.GetAsync(AppSettingKeys.StaleOpportunityDays, cancellationToken);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var openTasks = db.Tasks.AsNoTracking().Where(t => t.CompletedAt == null);
            var overdue = await openTasks.Where(t => t.DueDate < today).ToSummaries().ToListAsync(cancellationToken);
            var dueToday = await openTasks.Where(t => t.DueDate == today).ToSummaries().ToListAsync(cancellationToken);
            var withoutDueDate = await openTasks.Where(t => t.DueDate == null).ToSummaries().ToListAsync(cancellationToken);

            var stale = await LoadStaleOpportunitiesAsync(db, today, staleDays, cancellationToken);
            var recent = await LoadRecentlyEditedAsync(db, cancellationToken);

            return new Result(today, staleDays, overdue, dueToday, withoutDueDate, stale, recent);
        }

        /// <summary>
        /// Stale = open, not archived and at least <paramref name="staleDays"/> calendar days since the last direct
        /// activity (or since the day the request came in, without any activity; iteration 6 decision 11).
        /// </summary>
        private async Task<List<StaleOpportunity>> LoadStaleOpportunitiesAsync(
            ICrmDbContext db, DateOnly today, int staleDays, CancellationToken cancellationToken)
        {
            // Days since = today - local date of the last activity; ≥ N ⟺ last activity before the start of (today - N + 1).
            var threshold = clock.StartOfDayUtc(today.AddDays(1 - staleDays));
            var receivedThreshold = today.AddDays(-staleDays);

            var rows = await db.Opportunities
                .AsNoTracking()
                .Where(o => !o.IsArchived && o.Stage!.Status == StageStatus.Open)
                .Select(o => new
                {
                    o.Id,
                    o.Title,
                    StageName = o.Stage!.Name,
                    ClientName = o.ClientOrganization!.Name,
                    o.ReceivedOn,
                    LastActivityAt = db.Activities.Where(a => a.OpportunityId == o.Id).Max(a => (DateTimeOffset?)a.OccurredAt),
                })
                .Where(o => o.LastActivityAt == null ? o.ReceivedOn <= receivedThreshold : o.LastActivityAt < threshold)
                .ToListAsync(cancellationToken);

            return rows
                .Select(o =>
                {
                    var lastActivityAt = o.LastActivityAt ?? clock.StartOfDayUtc(o.ReceivedOn);
                    return new StaleOpportunity(o.Id, o.Title, o.StageName, o.ClientName, lastActivityAt, clock.DaysSince(lastActivityAt));
                })
                .OrderBy(o => o.LastActivityAt)
                .ThenBy(o => o.Id)
                .ToList();
        }

        private static async Task<List<RecentRecord>> LoadRecentlyEditedAsync(ICrmDbContext db, CancellationToken cancellationToken)
        {
            var contacts = (await db.Contacts.AsNoTracking()
                    .Where(c => !c.IsArchived)
                    .OrderByDescending(c => c.UpdatedAt)
                    .Take(RecentlyEditedLimit)
                    .Select(c => new { c.Id, c.FirstName, c.LastName, c.UpdatedAt })
                    .ToListAsync(cancellationToken))
                .Select(c => new RecentRecord(TimelineRecordType.Contact, c.Id, Names.Person(c.FirstName, c.LastName), c.UpdatedAt));

            var organizations = await db.Organizations.AsNoTracking()
                .Where(o => !o.IsArchived)
                .OrderByDescending(o => o.UpdatedAt)
                .Take(RecentlyEditedLimit)
                .Select(o => new RecentRecord(TimelineRecordType.Organization, o.Id, o.Name, o.UpdatedAt))
                .ToListAsync(cancellationToken);

            var opportunities = await db.Opportunities.AsNoTracking()
                .Where(o => !o.IsArchived)
                .OrderByDescending(o => o.UpdatedAt)
                .Take(RecentlyEditedLimit)
                .Select(o => new RecentRecord(TimelineRecordType.Opportunity, o.Id, o.Title, o.UpdatedAt))
                .ToListAsync(cancellationToken);

            return contacts
                .Concat(organizations)
                .Concat(opportunities)
                .OrderByDescending(r => r.UpdatedAt)
                .ThenByDescending(r => r.Id)
                .Take(RecentlyEditedLimit)
                .ToList();
        }
    }
}
