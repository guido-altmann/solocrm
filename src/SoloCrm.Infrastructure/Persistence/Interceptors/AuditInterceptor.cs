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
        var properties = FlattenProperties(entry).Where(p => !IgnoredProperties.Contains(p.Name));

        switch (entry.State)
        {
            case EntityState.Added:
                var created = properties
                    .Select(p => new AuditChange(p.Name, null, Format(p.Entry.CurrentValue)))
                    .Where(c => c.New is not null && !IsDefaultNoise(c.New))
                    .ToList();
                return new AuditEntry(entityType, entityId, AuditAction.Created, created, now);

            case EntityState.Modified:
                var changes = properties
                    .Where(p => p.Entry.IsModified)
                    .Select(p => new AuditChange(p.Name, Format(p.Entry.OriginalValue), Format(p.Entry.CurrentValue)))
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
                var deleted = properties
                    .Select(p => new AuditChange(p.Name, Format(p.Entry.OriginalValue), null))
                    .Where(c => c.Old is not null)
                    .ToList();
                return new AuditEntry(entityType, entityId, AuditAction.Deleted, deleted, now);

            default:
                return null;
        }
    }

    private static IEnumerable<(string Name, PropertyEntry Entry)> FlattenProperties(EntityEntry entry)
    {
        foreach (var property in entry.Properties)
        {
            yield return (property.Metadata.Name, property);
        }

        foreach (var complex in entry.ComplexProperties)
        {
            foreach (var nested in FlattenProperties(complex, complex.Metadata.Name))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<(string Name, PropertyEntry Entry)> FlattenProperties(ComplexPropertyEntry complex, string prefix)
    {
        foreach (var property in complex.Properties)
        {
            yield return ($"{prefix}.{property.Metadata.Name}", property);
        }

        foreach (var nestedComplex in complex.ComplexProperties)
        {
            foreach (var nested in FlattenProperties(nestedComplex, $"{prefix}.{nestedComplex.Metadata.Name}"))
            {
                yield return nested;
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
}
