using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;

namespace SoloCrm.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Records every change of an <see cref="IAuditable"/> entity as <see cref="AuditEntry"/> with field diffs (ADR-006).
/// The entries are added to the same <c>SaveChanges</c> call and therefore share its transaction.
/// Complex type members are diffed as individual fields (<c>Pricing.Amount</c>).
/// </summary>
public sealed class AuditInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    private static readonly HashSet<string> IgnoredProperties =
        [nameof(Entity.Id), nameof(Entity.CreatedAt), nameof(Entity.UpdatedAt)];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        // Materialize first: adding entries while enumerating the change tracker is not allowed.
        var auditEntries = context.ChangeTracker.Entries<IAuditable>()
            .Select(entry => CreateAuditEntry(entry, now))
            .OfType<AuditEntry>()
            .ToList();

        context.Set<AuditEntry>().AddRange(auditEntries);
    }

    private static AuditEntry? CreateAuditEntry(EntityEntry<IAuditable> entry, DateTimeOffset now)
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

    private static IEnumerable<Field> Fields(EntityEntry entry)
    {
        foreach (var property in entry.Properties)
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
}
