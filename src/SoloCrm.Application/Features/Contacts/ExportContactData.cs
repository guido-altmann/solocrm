using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Tasks;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// GDPR access request (US-19): everything stored about one contact as a stable JSON document (format version
/// <see cref="FormatVersion"/>, camelCase, timestamps in UTC as ISO 8601, enums as names). Contains the master data,
/// the activities and tasks with a direct reference, the opportunities with the contact as primary contact and the
/// audit entries of the contact and of its activities and tasks, including those deleted since (iteration 6
/// decision 1). Archived contacts are exported too.
/// </summary>
public static class ExportContactData
{
    /// <summary>Increased with every incompatible change of the document.</summary>
    public const int FormatVersion = 1;

    public sealed record Query(Guid ContactId);

    public sealed record Result(
        int FormatVersion,
        DateTimeOffset ExportedAt,
        ContactData Contact,
        IReadOnlyList<ActivityData> Activities,
        IReadOnlyList<TaskData> Tasks,
        IReadOnlyList<OpportunityData> Opportunities,
        IReadOnlyList<AuditEntryData> AuditEntries);

    public sealed record ContactData(
        Guid Id,
        string? FirstName,
        string? LastName,
        string? Email,
        string? Phone,
        string? JobTitle,
        string? LinkedInUrl,
        LeadSource? Source,
        bool IsArchived,
        AddressExport Address,
        IReadOnlyDictionary<string, string> ExtraFields,
        OrganizationData? Organization,
        IReadOnlyList<string> Tags,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record AddressExport(string? Street, string? Street2, string? PostalCode, string? City, string? Region, string? CountryCode);

    public sealed record OrganizationData(Guid Id, string Name, OrganizationType Type);

    public sealed record ActivityData(
        Guid Id,
        ActivityType Type,
        DateTimeOffset OccurredAt,
        string? Subject,
        string Body,
        Guid? OrganizationId,
        Guid? OpportunityId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record TaskData(
        Guid Id,
        string Title,
        DateOnly? DueDate,
        DateTimeOffset? CompletedAt,
        Guid? OrganizationId,
        Guid? OpportunityId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    /// <summary><paramref name="Role"/> is the role of the contact in the opportunity.</summary>
    public sealed record OpportunityData(Guid Id, string Title, string Stage, StageStatus StageStatus, ContactRole Role, DateTimeOffset CreatedAt);

    public enum ContactRole
    {
        PrimaryContact,
    }

    public sealed record AuditEntryData(
        Guid Id,
        string EntityType,
        Guid EntityId,
        AuditAction Action,
        DateTimeOffset OccurredAt,
        IReadOnlyList<AuditChange> Changes);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Umlauts stay readable; the file is downloaded, never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new UtcDateTimeOffsetConverter() },
    };

    /// <summary>The document as it is downloaded in the UI and returned by the REST API.</summary>
    public static string ToJson(Result export) => JsonSerializer.Serialize(export, JsonOptions);

    public sealed class Handler(ICrmDbContextFactory dbFactory, TimeProvider timeProvider) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var id = query.ContactId;

            var contact = await db.Contacts
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new
                {
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    c.Email,
                    c.Phone,
                    c.JobTitle,
                    c.LinkedInUrl,
                    c.Source,
                    c.IsArchived,
                    Address = new AddressExport(
                        c.Address.Street, c.Address.Street2, c.Address.PostalCode, c.Address.City, c.Address.Region, c.Address.CountryCode),
                    c.ExtraFields,
                    Organization = c.OrganizationId == null
                        ? null
                        : new OrganizationData(c.Organization!.Id, c.Organization.Name, c.Organization.Type),
                    c.CreatedAt,
                    c.UpdatedAt,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (contact is null)
            {
                return ContactErrors.NotFound;
            }

            var tags = await db.ContactTags
                .AsNoTracking()
                .Where(t => t.ContactId == id)
                .Select(t => t.Tag!.Name)
                .OrderBy(n => n)
                .ToListAsync(cancellationToken);

            var activities = await db.Activities
                .AsNoTracking()
                .Where(a => a.ContactId == id)
                .OrderBy(a => a.OccurredAt)
                .ThenBy(a => a.Id)
                .Select(a => new ActivityData(a.Id, a.Type, a.OccurredAt, a.Subject, a.Body, a.OrganizationId, a.OpportunityId, a.CreatedAt, a.UpdatedAt))
                .ToListAsync(cancellationToken);

            var tasks = await db.Tasks
                .AsNoTracking()
                .Where(t => t.ContactId == id)
                .OrderBy(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Select(t => new TaskData(t.Id, t.Title, t.DueDate, t.CompletedAt, t.OrganizationId, t.OpportunityId, t.CreatedAt, t.UpdatedAt))
                .ToListAsync(cancellationToken);

            var opportunities = await db.Opportunities
                .AsNoTracking()
                .Where(o => o.PrimaryContactId == id)
                .OrderBy(o => o.CreatedAt)
                .ThenBy(o => o.Id)
                .Select(o => new OpportunityData(o.Id, o.Title, o.Stage!.Name, o.Stage.Status, ContactRole.PrimaryContact, o.CreatedAt))
                .ToListAsync(cancellationToken);

            var auditEntries = await LoadAuditEntriesAsync(db, id, activities, tasks, cancellationToken);

            return new Result(
                FormatVersion,
                timeProvider.GetUtcNow(),
                new ContactData(
                    contact.Id,
                    contact.FirstName,
                    contact.LastName,
                    contact.Email,
                    contact.Phone,
                    contact.JobTitle,
                    contact.LinkedInUrl,
                    contact.Source,
                    contact.IsArchived,
                    contact.Address,
                    new SortedDictionary<string, string>(contact.ExtraFields.ToDictionary(), StringComparer.Ordinal),
                    contact.Organization,
                    tags,
                    contact.CreatedAt,
                    contact.UpdatedAt),
                activities,
                tasks,
                opportunities,
                auditEntries);
        }

        /// <summary>Entries of the contact and of its activities and tasks, also of those deleted since.</summary>
        private static async Task<List<AuditEntryData>> LoadAuditEntriesAsync(
            ICrmDbContext db, Guid contactId, List<ActivityData> activities, List<TaskData> tasks, CancellationToken cancellationToken)
        {
            var activityIds = await db.AuditedRecordIds(nameof(Activity), nameof(Activity.ContactId), contactId).ToListAsync(cancellationToken);
            var taskIds = await db.AuditedRecordIds(nameof(TaskItem), nameof(TaskItem.ContactId), contactId).ToListAsync(cancellationToken);
            var activityAndTaskIds = activityIds
                .Concat(activities.Select(a => a.Id))
                .Concat(taskIds)
                .Concat(tasks.Select(t => t.Id))
                .Distinct()
                .ToList();

            return await db.AuditEntries
                .AsNoTracking()
                .Where(a => (a.EntityType == nameof(Contact) && a.EntityId == contactId)
                    || ((a.EntityType == nameof(Activity) || a.EntityType == nameof(TaskItem)) && activityAndTaskIds.Contains(a.EntityId)))
                .OrderBy(a => a.OccurredAt)
                .ThenBy(a => a.Id)
                .Select(a => new AuditEntryData(a.Id, a.EntityType, a.EntityId, a.Action, a.OccurredAt, a.Changes))
                .ToListAsync(cancellationToken);
        }
    }

    /// <summary>Writes every timestamp in UTC with <c>Z</c> (e.g. <c>2026-10-02T08:15:00.000Z</c>).</summary>
    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
