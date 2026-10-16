using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Nextcloud;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>State of the Nextcloud connection for the settings page (ADR-014).</summary>
public static class GetNextcloudStatus
{
    public sealed record Query;

    /// <param name="Connection">The connected account, or <c>null</c>.</param>
    public sealed record Result(Connection? Connection);

    /// <param name="IsCredentialRejected">The server rejected the app password; the sync is paused.</param>
    /// <param name="Contacts">Contacts currently mirrored to the address book.</param>
    /// <param name="Tasks">Tasks currently mirrored to the task list.</param>
    public sealed record Connection(
        string ServerUrl,
        string UserName,
        DateTimeOffset ConnectedAt,
        DateTimeOffset? LastSyncedAt,
        string? LastError,
        bool IsCredentialRejected,
        int Contacts,
        int Tasks);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var connection = await db.NextcloudConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (connection is null)
            {
                return new Result(null);
            }

            var counts = await db.NextcloudSyncItems
                .GroupBy(i => i.EntityType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Type, g => g.Count, cancellationToken);
            return new Result(new Connection(
                connection.ServerUrl,
                connection.UserName,
                connection.ConnectedAt,
                connection.LastSyncedAt,
                connection.LastError,
                connection.IsCredentialRejected,
                counts.GetValueOrDefault(NextcloudItemType.Contact),
                counts.GetValueOrDefault(NextcloudItemType.Task)));
        }
    }
}
