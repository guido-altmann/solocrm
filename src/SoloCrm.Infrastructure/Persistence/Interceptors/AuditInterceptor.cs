using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Records every change of an <see cref="IAuditable"/> entity as <see cref="AuditEntry"/> with field diffs (ADR-006).
/// The entries are added to the same <c>SaveChanges</c> call and therefore share its transaction.
/// Complex type members are diffed as individual fields (<c>Pricing.Amount</c>); generated columns are skipped.
/// Adding or removing a tag (<see cref="ITagAssignment"/>) is recorded as <c>Updated</c> of the tagged record with the
/// field <see cref="TagsField"/> and the tag name as new or old value.
/// <para>
/// Deleting an <see cref="IErasable"/> entity is a GDPR erasure (US-20): its <c>Deleted</c> entry has no field values,
/// and so have the entries of auditable records deleted with it whose foreign key cascades from it (activities and
/// tasks of a contact). All earlier entries of these records are anonymized in the same save, including those of
/// records deleted before (found by the foreign key in their <c>Created</c>/<c>Deleted</c> entry). Entries of other
/// records that only contain the id (e.g. <c>PrimaryContactId</c> of an opportunity) stay (iteration 6 decision 3).
/// </para>
/// </summary>
public sealed class AuditInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public const string TagsField = "Tags";

    private static readonly HashSet<string> IgnoredProperties =
        [nameof(Entity.Id), nameof(Entity.CreatedAt), nameof(Entity.UpdatedAt)];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            var erasure = Erasure.Collect(context);
            AddAuditEntries(context, erasure);
            if (!erasure.IsEmpty)
            {
                var ids = erasure.HistoryIds(context);
                foreach (var entry in context.Set<AuditEntry>().Where(a => ids.Contains(a.EntityId)))
                {
                    entry.Anonymize();
                }
            }
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        return SavingChangesCoreAsync(eventData, result, cancellationToken);
    }

    private async ValueTask<InterceptionResult<int>> SavingChangesCoreAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken)
    {
        if (eventData.Context is { } context)
        {
            var erasure = Erasure.Collect(context);
            AddAuditEntries(context, erasure);
            if (!erasure.IsEmpty)
            {
                var ids = await erasure.HistoryIdsAsync(context, cancellationToken);
                await foreach (var entry in context.Set<AuditEntry>().Where(a => ids.Contains(a.EntityId)).AsAsyncEnumerable().WithCancellation(cancellationToken))
                {
                    entry.Anonymize();
                }
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(DbContext context, Erasure erasure)
    {
        var now = timeProvider.GetUtcNow();

        // Materialize first: adding entries while enumerating the change tracker is not allowed.
        var auditEntries = context.ChangeTracker.Entries<IAuditable>()
            .Select(entry => CreateAuditEntry(entry, erasure, now))
            .OfType<AuditEntry>()
            .Concat(CreateTagAuditEntries(context, erasure, now))
            .ToList();

        context.Set<AuditEntry>().AddRange(auditEntries);
    }

    private static AuditEntry? CreateAuditEntry(EntityEntry<IAuditable> entry, Erasure erasure, DateTimeOffset now)
    {
        var entityType = entry.Metadata.ClrType.Name;
        var entityId = entry.Entity.Id;
        var fields = Fields(entry).Where(f => !IgnoredProperties.Contains(f.Name));

        switch (entry.State)
        {
            case EntityState.Added:
                var created = fields
                    .Select(f => new AuditChange(f.Name, null, Format(f.Current)))
                    .Where(c => c.New is not null && !IsDefaultNoise(c.New))
                    .ToList();
                return new AuditEntry(entityType, entityId, AuditAction.Created, created, now);

            case EntityState.Modified:
                // Compared by value instead of IsModified: for a complex type that becomes null, EF reports
                // unchanged CLR defaults (e.g. PricingModel.Hourly) as not modified.
                var changes = fields
                    .Select(f => new AuditChange(f.Name, Format(f.Original), Format(f.Current)))
                    .Where(c => c.Old != c.New)
                    .ToList();
                if (changes.Count == 0)
                {
                    return null;
                }

                var action = changes.Any(c => c is { Field: nameof(ArchivableEntity.IsArchived), New: "true" })
                    ? AuditAction.Archived
                    : AuditAction.Updated;
                return new AuditEntry(entityType, entityId, action, changes, now);

            case EntityState.Deleted when erasure.Contains(entityId):
                return new AuditEntry(entityType, entityId, AuditAction.Deleted, [], now);

            case EntityState.Deleted:
                var deleted = fields
                    .Select(f => new AuditChange(f.Name, Format(f.Original), null))
                    .Where(c => c.Old is not null)
                    .ToList();
                return new AuditEntry(entityType, entityId, AuditAction.Deleted, deleted, now);

            default:
                return null;
        }
    }

    /// <summary>One <c>Updated</c> entry per tagged record, listing all tags added or removed in this save.</summary>
    private static IEnumerable<AuditEntry> CreateTagAuditEntries(DbContext context, Erasure erasure, DateTimeOffset now) =>
        context.ChangeTracker.Entries<ITagAssignment>()
            .Where(e => (e.State is EntityState.Added or EntityState.Deleted) && !erasure.Contains(e.Entity.RecordId))
            .GroupBy(e => (e.Entity.RecordType, e.Entity.RecordId))
            .Select(record => new AuditEntry(
                record.Key.RecordType,
                record.Key.RecordId,
                AuditAction.Updated,
                [
                    .. record.Select(e =>
                    {
                        var tag = e.Entity.Tag?.Name ?? e.Entity.TagId.ToString();
                        return e.State == EntityState.Added
                            ? new AuditChange(TagsField, null, tag)
                            : new AuditChange(TagsField, tag, null);
                    }),
                ],
                now));

    private static IEnumerable<Field> Fields(EntityEntry entry)
    {
        // Generated columns (e.g. the search vector, ADR-007) are derived data, not changes.
        foreach (var property in entry.Properties.Where(p => p.Metadata.GetComputedColumnSql() is null))
        {
            yield return new Field(property.Metadata.Name, property.OriginalValue, property.CurrentValue);
        }

        if (!entry.ComplexProperties.Any())
        {
            yield break;
        }

        // ComplexPropertyEntry has no original value; the materialized original entity tells whether it was null.
        var original = entry.State == EntityState.Added ? null : entry.OriginalValues.ToObject();
        foreach (var complex in entry.ComplexProperties)
        {
            var originalValue = original is null ? null : complex.Metadata.PropertyInfo?.GetValue(original);
            foreach (var field in Fields(complex, complex.Metadata.Name, originalValue, complex.CurrentValue))
            {
                yield return field;
            }
        }
    }

    /// <summary>
    /// Members of a complex type that is <c>null</c> report CLR defaults in EF Core 10; they are recorded as <c>null</c>.
    /// </summary>
    private static IEnumerable<Field> Fields(ComplexPropertyEntry complex, string prefix, object? originalValue, object? currentValue)
    {
        foreach (var property in complex.Properties)
        {
            yield return new Field(
                $"{prefix}.{property.Metadata.Name}",
                originalValue is null ? null : property.OriginalValue,
                currentValue is null ? null : property.CurrentValue);
        }

        foreach (var nested in complex.ComplexProperties)
        {
            var nestedOriginal = originalValue is null ? null : nested.Metadata.PropertyInfo?.GetValue(originalValue);
            foreach (var field in Fields(nested, $"{prefix}.{nested.Metadata.Name}", nestedOriginal, nested.CurrentValue))
            {
                yield return field;
            }
        }
    }

    /// <summary>Defaults that carry no information on creation (e.g. <c>IsArchived = false</c>, no extra fields).</summary>
    private static bool IsDefaultNoise(string value) => value is "false" or "{}";

    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        decimal d => d.ToString("0.############", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IReadOnlyDictionary<string, string> dictionary =>
            JsonSerializer.Serialize(new SortedDictionary<string, string>(dictionary.ToDictionary(), StringComparer.Ordinal)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private readonly record struct Field(string Name, object? Original, object? Current);

    /// <summary>The records erased in one save: deleted <see cref="IErasable"/> entities and their cascading dependents.</summary>
    private sealed class Erasure
    {
        private readonly HashSet<Guid> _ids;
        private readonly List<(string EntityType, string ForeignKey, Guid PrincipalId)> _dependents;

        private Erasure(HashSet<Guid> ids, List<(string, string, Guid)> dependents)
        {
            _ids = ids;
            _dependents = dependents;
        }

        public bool IsEmpty => _ids.Count == 0;

        public bool Contains(Guid id) => _ids.Contains(id);

        public static Erasure Collect(DbContext context)
        {
            var erased = context.ChangeTracker.Entries<IErasable>()
                .Where(e => e.State == EntityState.Deleted)
                .ToList();
            if (erased.Count == 0)
            {
                return new Erasure([], []);
            }

            var ids = erased.Select(e => e.Entity.Id).ToHashSet();

            // Auditable records that the database deletes with an erased entity (cascading foreign key).
            var cascades = erased
                .Select(e => e.Metadata)
                .Distinct()
                .SelectMany(t => t.GetReferencingForeignKeys())
                .Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade
                    && typeof(IAuditable).IsAssignableFrom(fk.DeclaringEntityType.ClrType)
                    && fk.Properties.Count == 1)
                .ToList();

            var dependents = (
                from fk in cascades
                from e in erased
                where fk.PrincipalEntityType.IsAssignableFrom(e.Metadata)
                select (fk.DeclaringEntityType.ClrType.Name, fk.Properties[0].Name, e.Entity.Id)).ToList();

            foreach (var entry in context.ChangeTracker.Entries<IAuditable>().Where(e => e.State == EntityState.Deleted))
            {
                var linked = cascades
                    .Where(fk => fk.DeclaringEntityType.IsAssignableFrom(entry.Metadata))
                    .Any(fk => entry.Property(fk.Properties[0].Name).OriginalValue is Guid id && ids.Contains(id));
                if (linked)
                {
                    ids.Add(entry.Entity.Id);
                }
            }

            return new Erasure(ids, dependents);
        }

        /// <summary>Erased records plus earlier deleted dependents, found by the foreign key in their audit entries.</summary>
        public List<Guid> HistoryIds(DbContext context) =>
            [.. _ids, .. _dependents.SelectMany(d => DependentQuery(context, d).ToList())];

        public async Task<List<Guid>> HistoryIdsAsync(DbContext context, CancellationToken cancellationToken)
        {
            var result = new List<Guid>(_ids);
            foreach (var dependent in _dependents)
            {
                result.AddRange(await DependentQuery(context, dependent).ToListAsync(cancellationToken));
            }

            return result;
        }

        private static IQueryable<Guid> DependentQuery(DbContext context, (string EntityType, string ForeignKey, Guid PrincipalId) dependent) =>
            AuditHistory.LinkedRecordIds(context, dependent.EntityType, dependent.ForeignKey, dependent.PrincipalId);
    }
}
