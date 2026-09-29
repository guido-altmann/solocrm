using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Timeline;

public enum TimelineRecordType
{
    Contact,
    Organization,
    Opportunity,
}

public enum TimelineEntryKind
{
    Activity,
    TaskCreated,
    TaskCompleted,
    Created,
    Updated,
    Archived,
    Restored,
}

/// <summary>
/// The timeline of a contact, organization or opportunity (SPEC 2.5): activities, tasks (created/completed) and the
/// visible audit entries, newest first, paged by cursor („Mehr laden“).
/// <list type="bullet">
/// <item>Opportunity: only entries with a direct reference.</item>
/// <item>Contact: direct entries plus those of opportunities with this contact as primary contact.</item>
/// <item>Organization: direct entries plus those of its contacts and of opportunities with it as client or agency
/// („via …“). „Angelegt“ of its contacts is left out (iteration 3, decision 6).</item>
/// </list>
/// Each source is queried with the cursor as row value comparison and limited separately, then merged.
/// </summary>
public static class GetTimeline
{
    public const int DefaultLimit = 30;
    public const int MaxLimit = 100;

    /// <summary>Position in the timeline: ordered by time, then source, then id (all descending).</summary>
    public sealed record Cursor(DateTimeOffset OccurredAt, TimelineSource Source, Guid Id);

    public sealed record Query(TimelineRecordType RecordType, Guid RecordId, Cursor? Before = null, int Limit = DefaultLimit);

    /// <summary>The related record an aggregated entry comes from („via Max Mustermann“).</summary>
    public sealed record Via(TimelineRecordType Type, Guid Id, string Name);

    /// <summary>A readable audit change, e.g. („Phase“, „Beworben“, „Im Gespräch“).</summary>
    public sealed record Change(string Label, string Old, string New);

    /// <param name="SourceId">The activity or task; for audit entries the audited record.</param>
    public sealed record Entry(
        TimelineEntryKind Kind,
        Guid SourceId,
        DateTimeOffset OccurredAt,
        Via? Via,
        Cursor Position,
        ActivityType? ActivityType = null,
        string? Subject = null,
        string? Body = null,
        string? TaskTitle = null,
        IReadOnlyList<Change>? Changes = null);

    public sealed record Result(IReadOnlyList<Entry> Entries, Cursor? Next);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.RecordType)
                .IsInEnum()
                .WithMessage("Unbekannter Objekttyp.");

            RuleFor(q => q.Limit)
                .InclusiveBetween(1, MaxLimit)
                .WithMessage($"Es können höchstens {MaxLimit} Einträge abgefragt werden.");
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Query> validator) : IQueryHandler<Query, Result>
    {
        /// <summary>Audit entries are filtered in memory; they are read in batches until a page is full.</summary>
        private const int AuditBatchSize = 100;

        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(query, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var scope = await LoadScopeAsync(db, query, cancellationToken);
            if (scope is null)
            {
                return TimelineErrors.NotFound;
            }

            var take = query.Limit + 1;
            var entries = new List<Entry>();
            entries.AddRange(await LoadActivitiesAsync(db, scope, query.Before, take, cancellationToken));
            entries.AddRange(await LoadTasksCreatedAsync(db, scope, query.Before, take, cancellationToken));
            entries.AddRange(await LoadTasksCompletedAsync(db, scope, query.Before, take, cancellationToken));
            entries.AddRange(await LoadAuditEntriesAsync(db, scope, query.Before, take, cancellationToken));

            var page = entries
                .OrderByDescending(e => e.Position.OccurredAt)
                .ThenByDescending(e => e.Position.Source)
                .ThenByDescending(e => e.Position.Id)
                .Take(take)
                .ToList();

            Cursor? next = null;
            if (page.Count > query.Limit)
            {
                page.RemoveAt(query.Limit);
                next = page[^1].Position;
            }

            return new Result(page, next);
        }

        private static async Task<Scope?> LoadScopeAsync(ICrmDbContext db, Query query, CancellationToken cancellationToken)
        {
            var id = query.RecordId;
            switch (query.RecordType)
            {
                case TimelineRecordType.Contact:
                    if (!await db.Contacts.AnyAsync(c => c.Id == id, cancellationToken))
                    {
                        return null;
                    }

                    var ofContact = await db.Opportunities.AsNoTracking()
                        .Where(o => o.PrimaryContactId == id)
                        .Select(o => new Via(TimelineRecordType.Opportunity, o.Id, o.Title))
                        .ToListAsync(cancellationToken);
                    return new Scope(query.RecordType, id, [id], [], [.. ofContact.Select(v => v.Id)], ofContact, []);

                case TimelineRecordType.Organization:
                    if (!await db.Organizations.AnyAsync(o => o.Id == id, cancellationToken))
                    {
                        return null;
                    }

                    var contacts = (await db.Contacts.AsNoTracking()
                            .Where(c => c.OrganizationId == id)
                            .Select(c => new { c.Id, c.FirstName, c.LastName })
                            .ToListAsync(cancellationToken))
                        .Select(c => new Via(TimelineRecordType.Contact, c.Id, Names.Person(c.FirstName, c.LastName)))
                        .ToList();
                    var ofOrganization = await db.Opportunities.AsNoTracking()
                        .Where(o => o.ClientOrganizationId == id || o.AgencyOrganizationId == id)
                        .Select(o => new Via(TimelineRecordType.Opportunity, o.Id, o.Title))
                        .ToListAsync(cancellationToken);
                    var contactIds = contacts.Select(c => c.Id).ToList();
                    return new Scope(
                        query.RecordType,
                        id,
                        contactIds,
                        [id],
                        [.. ofOrganization.Select(v => v.Id)],
                        [.. contacts, .. ofOrganization],
                        [.. contactIds]);

                default:
                    return await db.Opportunities.AnyAsync(o => o.Id == id, cancellationToken)
                        ? new Scope(query.RecordType, id, [], [], [id], [], [])
                        : null;
            }
        }

        private static async Task<IEnumerable<Entry>> LoadActivitiesAsync(
            ICrmDbContext db, Scope scope, Cursor? before, int take, CancellationToken cancellationToken)
        {
            var activities = db.Activities.AsNoTracking().Where(a =>
                scope.ContactIds.Contains(a.ContactId)
                || scope.OrganizationIds.Contains(a.OrganizationId)
                || scope.OpportunityIds.Contains(a.OpportunityId));

            if (before is not null)
            {
                activities = before.Source == TimelineSource.Activity
                    ? activities.Where(a => EF.Functions.LessThan(ValueTuple.Create(a.OccurredAt, a.Id), ValueTuple.Create(before.OccurredAt, before.Id)))
                    : TimelineSource.Activity < before.Source
                        ? activities.Where(a => a.OccurredAt <= before.OccurredAt)
                        : activities.Where(a => a.OccurredAt < before.OccurredAt);
            }

            var rows = await activities
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .Take(take)
                .Select(a => new { a.Id, a.Type, a.OccurredAt, a.Subject, a.Body, a.ContactId, a.OrganizationId, a.OpportunityId })
                .ToListAsync(cancellationToken);

            return rows.Select(a => new Entry(
                TimelineEntryKind.Activity,
                a.Id,
                a.OccurredAt,
                scope.ViaFor(new LinkedRecords(a.ContactId, a.OrganizationId, a.OpportunityId)),
                new Cursor(a.OccurredAt, TimelineSource.Activity, a.Id),
                ActivityType: a.Type,
                Subject: a.Subject,
                Body: a.Body));
        }

        private static async Task<IEnumerable<Entry>> LoadTasksCreatedAsync(
            ICrmDbContext db, Scope scope, Cursor? before, int take, CancellationToken cancellationToken)
        {
            var tasks = db.Tasks.AsNoTracking().Where(t =>
                scope.ContactIds.Contains(t.ContactId)
                || scope.OrganizationIds.Contains(t.OrganizationId)
                || scope.OpportunityIds.Contains(t.OpportunityId));

            if (before is not null)
            {
                tasks = before.Source == TimelineSource.TaskCreated
                    ? tasks.Where(t => EF.Functions.LessThan(ValueTuple.Create(t.CreatedAt, t.Id), ValueTuple.Create(before.OccurredAt, before.Id)))
                    : TimelineSource.TaskCreated < before.Source
                        ? tasks.Where(t => t.CreatedAt <= before.OccurredAt)
                        : tasks.Where(t => t.CreatedAt < before.OccurredAt);
            }

            var rows = await tasks
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Take(take)
                .Select(t => new { t.Id, t.Title, t.CreatedAt, t.ContactId, t.OrganizationId, t.OpportunityId })
                .ToListAsync(cancellationToken);

            return rows.Select(t => new Entry(
                TimelineEntryKind.TaskCreated,
                t.Id,
                t.CreatedAt,
                scope.ViaFor(new LinkedRecords(t.ContactId, t.OrganizationId, t.OpportunityId)),
                new Cursor(t.CreatedAt, TimelineSource.TaskCreated, t.Id),
                TaskTitle: t.Title));
        }

        private static async Task<IEnumerable<Entry>> LoadTasksCompletedAsync(
            ICrmDbContext db, Scope scope, Cursor? before, int take, CancellationToken cancellationToken)
        {
            var tasks = db.Tasks.AsNoTracking().Where(t =>
                t.CompletedAt != null
                && (scope.ContactIds.Contains(t.ContactId)
                    || scope.OrganizationIds.Contains(t.OrganizationId)
                    || scope.OpportunityIds.Contains(t.OpportunityId)));

            if (before is not null)
            {
                tasks = before.Source == TimelineSource.TaskCompleted
                    ? tasks.Where(t => EF.Functions.LessThan(ValueTuple.Create(t.CompletedAt!.Value, t.Id), ValueTuple.Create(before.OccurredAt, before.Id)))
                    : TimelineSource.TaskCompleted < before.Source
                        ? tasks.Where(t => t.CompletedAt <= before.OccurredAt)
                        : tasks.Where(t => t.CompletedAt < before.OccurredAt);
            }

            var rows = await tasks
                .OrderByDescending(t => t.CompletedAt)
                .ThenByDescending(t => t.Id)
                .Take(take)
                .Select(t => new { t.Id, t.Title, CompletedAt = t.CompletedAt!.Value, t.ContactId, t.OrganizationId, t.OpportunityId })
                .ToListAsync(cancellationToken);

            return rows.Select(t => new Entry(
                TimelineEntryKind.TaskCompleted,
                t.Id,
                t.CompletedAt,
                scope.ViaFor(new LinkedRecords(t.ContactId, t.OrganizationId, t.OpportunityId)),
                new Cursor(t.CompletedAt, TimelineSource.TaskCompleted, t.Id),
                TaskTitle: t.Title));
        }

        /// <summary>
        /// Reads the audit entries of all records in scope in batches and keeps the visible ones until
        /// <paramref name="take"/> are found (invisible changes such as a new phone number are skipped).
        /// </summary>
        private static async Task<IEnumerable<Entry>> LoadAuditEntriesAsync(
            ICrmDbContext db, Scope scope, Cursor? before, int take, CancellationToken cancellationToken)
        {
            var visible = new List<AuditEntry>();
            var position = before;
            while (visible.Count < take)
            {
                var entries = db.AuditEntries.AsNoTracking().Where(a =>
                    (a.EntityType == nameof(Contact) && scope.ContactIds.Contains(a.EntityId))
                    || (a.EntityType == nameof(Organization) && scope.OrganizationIds.Contains(a.EntityId))
                    || (a.EntityType == nameof(Opportunity) && scope.OpportunityIds.Contains(a.EntityId)));

                if (position is not null)
                {
                    // Audit is the lowest source, so audit entries at the time of another source's cursor still follow it.
                    var cursor = position;
                    entries = cursor.Source == TimelineSource.Audit
                        ? entries.Where(a => EF.Functions.LessThan(ValueTuple.Create(a.OccurredAt, a.Id), ValueTuple.Create(cursor.OccurredAt, cursor.Id)))
                        : entries.Where(a => a.OccurredAt <= cursor.OccurredAt);
                }

                var batch = await entries
                    .OrderByDescending(a => a.OccurredAt)
                    .ThenByDescending(a => a.Id)
                    .Take(AuditBatchSize)
                    .ToListAsync(cancellationToken);

                visible.AddRange(batch.Where(a => IsVisible(a, scope)));
                if (batch.Count < AuditBatchSize)
                {
                    break;
                }

                position = new Cursor(batch[^1].OccurredAt, TimelineSource.Audit, batch[^1].Id);
            }

            visible = visible.Take(take).ToList();
            var formatter = await AuditFormatter.CreateAsync(db, visible, cancellationToken);

            return visible.Select(a =>
            {
                var (kind, changes) = formatter.Describe(a);
                return new Entry(
                    kind,
                    a.EntityId,
                    a.OccurredAt,
                    scope.ViaFor(a.EntityId),
                    new Cursor(a.OccurredAt, TimelineSource.Audit, a.Id),
                    Changes: changes);
            });
        }

        private static bool IsVisible(AuditEntry entry, Scope scope) => entry.Action switch
        {
            AuditAction.Created => !scope.WithoutCreated.Contains(entry.EntityId),
            AuditAction.Archived => true,
            AuditAction.Updated => entry.Changes.Any(TimelineAuditFilter.IsVisible),
            _ => false,
        };
    }

    /// <summary>The records whose entries make up one timeline.</summary>
    private sealed class Scope(
        TimelineRecordType recordType,
        Guid recordId,
        List<Guid> contactIds,
        List<Guid> organizationIds,
        List<Guid> opportunityIds,
        List<Via> related,
        HashSet<Guid> withoutCreated)
    {
        private readonly Dictionary<Guid, Via> _related = related.ToDictionary(v => v.Id);

        // Nullable element types so that EF translates Contains on the nullable foreign keys to "= ANY (…)".
        public List<Guid?> ContactIds { get; } = [.. contactIds.Select(id => (Guid?)id)];

        public List<Guid?> OrganizationIds { get; } = [.. organizationIds.Select(id => (Guid?)id)];

        public List<Guid?> OpportunityIds { get; } = [.. opportunityIds.Select(id => (Guid?)id)];

        /// <summary>Records whose „Angelegt“ entry is hidden (contacts in the organization timeline).</summary>
        public HashSet<Guid> WithoutCreated { get; } = withoutCreated;

        /// <summary><c>null</c> for a direct reference, otherwise the related record.</summary>
        public Via? ViaFor(LinkedRecords linkedTo)
        {
            var direct = recordType switch
            {
                TimelineRecordType.Contact => linkedTo.ContactId,
                TimelineRecordType.Organization => linkedTo.OrganizationId,
                _ => linkedTo.OpportunityId,
            };

            if (direct == recordId)
            {
                return null;
            }

            return new[] { linkedTo.ContactId, linkedTo.OpportunityId, linkedTo.OrganizationId }
                .Select(id => id is { } value ? _related.GetValueOrDefault(value) : null)
                .FirstOrDefault(v => v is not null);
        }

        public Via? ViaFor(Guid entityId) => entityId == recordId ? null : _related.GetValueOrDefault(entityId);
    }

    /// <summary>
    /// Turns audit entries into readable changes. Stage and organization ids are resolved to names; for pricing
    /// and duration the full state before and after is replayed from the audit history of the opportunity,
    /// because the diff only contains the changed members (e.g. only <c>Pricing.Amount</c>).
    /// </summary>
    private sealed class AuditFormatter(
        IReadOnlyDictionary<Guid, string> stages,
        IReadOnlyDictionary<Guid, string> organizations,
        IReadOnlyDictionary<Guid, (IReadOnlyDictionary<string, string?> Before, IReadOnlyDictionary<string, string?> After)> states)
    {
        public static async Task<AuditFormatter> CreateAsync(ICrmDbContext db, IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken)
        {
            var changes = entries.Where(e => e.Action == AuditAction.Updated).SelectMany(e => e.Changes).ToList();

            var stageIds = IdsOf(changes, TimelineAuditFilter.StageField);
            var stages = stageIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await db.Stages.AsNoTracking().Where(s => stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

            var organizationIds = IdsOf(changes, TimelineAuditFilter.OrganizationField);
            var organizations = organizationIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await db.Organizations.AsNoTracking().Where(o => organizationIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);

            var needingState = entries
                .Where(e => e.Action == AuditAction.Updated && TimelineAuditFilter.NeedsState(e.Changes))
                .ToList();
            var states = await ReplayAsync(db, needingState, cancellationToken);

            return new AuditFormatter(stages, organizations, states);
        }

        public (TimelineEntryKind Kind, IReadOnlyList<Change> Changes) Describe(AuditEntry entry)
        {
            switch (entry.Action)
            {
                case AuditAction.Created:
                    return (TimelineEntryKind.Created, []);
                case AuditAction.Archived:
                    return (TimelineEntryKind.Archived, []);
            }

            var visible = entry.Changes.Where(TimelineAuditFilter.IsVisible).ToList();
            var changes = new List<Change>();

            if (visible.FirstOrDefault(c => c.Field == TimelineAuditFilter.StageField) is { } stage)
            {
                changes.Add(new Change("Phase", NameOf(stages, stage.Old), NameOf(stages, stage.New)));
            }

            if (states.TryGetValue(entry.Id, out var state))
            {
                if (visible.Any(c => c.Field.StartsWith(TimelineAuditFilter.PricingPrefix, StringComparison.Ordinal)))
                {
                    changes.Add(new Change("Preis", TimelineAuditFilter.FormatPricing(state.Before), TimelineAuditFilter.FormatPricing(state.After)));
                }

                if (visible.Any(c => c.Field.StartsWith(TimelineAuditFilter.DurationPrefix, StringComparison.Ordinal)))
                {
                    changes.Add(new Change("Laufzeit", TimelineAuditFilter.FormatDuration(state.Before), TimelineAuditFilter.FormatDuration(state.After)));
                }
            }

            if (visible.FirstOrDefault(c => c.Field == TimelineAuditFilter.OrganizationField) is { } organization)
            {
                changes.Add(new Change("Firma", NameOf(organizations, organization.Old), NameOf(organizations, organization.New)));
            }

            var restored = visible.Any(c => c is { Field: nameof(ArchivableEntity.IsArchived), New: "false" });
            return (restored ? TimelineEntryKind.Restored : TimelineEntryKind.Updated, changes);
        }

        private static List<Guid> IdsOf(IEnumerable<AuditChange> changes, string field) => changes
            .Where(c => c.Field == field)
            .SelectMany(c => new[] { c.Old, c.New })
            .Select(v => Guid.TryParse(v, out var id) ? id : (Guid?)null)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        private static string NameOf(IReadOnlyDictionary<Guid, string> names, string? id) =>
            id is null ? TimelineAuditFilter.Missing
            : Guid.TryParse(id, out var value) && names.TryGetValue(value, out var name) ? name
            : TimelineAuditFilter.Deleted;

        private static async Task<Dictionary<Guid, (IReadOnlyDictionary<string, string?> Before, IReadOnlyDictionary<string, string?> After)>> ReplayAsync(
            ICrmDbContext db, List<AuditEntry> entries, CancellationToken cancellationToken)
        {
            var result = new Dictionary<Guid, (IReadOnlyDictionary<string, string?>, IReadOnlyDictionary<string, string?>)>();
            if (entries.Count == 0)
            {
                return result;
            }

            var wanted = entries.Select(e => e.Id).ToHashSet();
            var entityIds = entries.Select(e => e.EntityId).Distinct().ToList();
            var history = await db.AuditEntries.AsNoTracking()
                .Where(a => entityIds.Contains(a.EntityId))
                .OrderBy(a => a.OccurredAt)
                .ThenBy(a => a.Id)
                .ToListAsync(cancellationToken);

            foreach (var group in history.GroupBy(a => a.EntityId))
            {
                var state = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var entry in group)
                {
                    var before = new Dictionary<string, string?>(state, StringComparer.Ordinal);
                    foreach (var change in entry.Changes)
                    {
                        state[change.Field] = change.New;
                    }

                    if (wanted.Contains(entry.Id))
                    {
                        result[entry.Id] = (before, new Dictionary<string, string?>(state, StringComparer.Ordinal));
                    }
                }
            }

            return result;
        }
    }
}

/// <summary>Source of a timeline entry; orders entries with the same time (higher first).</summary>
public enum TimelineSource
{
    Audit,
    TaskCreated,
    Activity,
    TaskCompleted,
}

public static class TimelineErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Timeline.NotFound", "Der Datensatz wurde nicht gefunden.");
}
