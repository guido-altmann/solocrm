using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>
/// Removes the connection and the mapping (ADR-014). The collections in Nextcloud stay as they are; the settings
/// point that out before disconnecting.
/// </summary>
public static class DisconnectNextcloud
{
    public sealed record Command;

    public sealed record Result;

    public sealed class Handler(ICrmDbContextFactory dbFactory) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await db.NextcloudConnections.ExecuteDeleteAsync(cancellationToken);
            await db.NextcloudSyncItems.ExecuteDeleteAsync(cancellationToken);
            return new Result();
        }
    }
}
