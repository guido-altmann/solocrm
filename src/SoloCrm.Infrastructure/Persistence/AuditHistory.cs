using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SoloCrm.Infrastructure.Persistence;

/// <summary>Queries over the field values stored in <c>audit_entries.changes</c>.</summary>
internal static class AuditHistory
{
    /// <summary>
    /// Ids of the audited records of <paramref name="entityType"/> whose foreign key <paramref name="foreignKey"/> points
    /// or pointed to <paramref name="principalId"/>, including records deleted since. Foreign keys of activities and
    /// tasks are set on creation and never change, so the <c>Created</c> (new value) or the <c>Deleted</c> entry
    /// (old value) of every such record contains it; jsonb containment (<c>@&gt;</c>) keeps the filter in the database.
    /// </summary>
    public static IQueryable<Guid> LinkedRecordIds(DbContext context, string entityType, string foreignKey, Guid principalId)
    {
        // Same shape as JsonColumn stores AuditChange (camelCase), reduced to the members to match.
        var id = principalId.ToString();
        var created = JsonSerializer.Serialize(new[] { new Dictionary<string, string> { ["field"] = foreignKey, ["new"] = id } });
        var deleted = JsonSerializer.Serialize(new[] { new Dictionary<string, string> { ["field"] = foreignKey, ["old"] = id } });

        return context.Database.SqlQuery<Guid>($"""
            SELECT DISTINCT entity_id AS "Value" FROM audit_entries
            WHERE entity_type = {entityType} AND (changes @> {created}::jsonb OR changes @> {deleted}::jsonb)
            """);
    }
}
