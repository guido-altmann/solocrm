using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Nextcloud;
using SoloCrm.Infrastructure.Persistence;

namespace SoloCrm.Infrastructure.Nextcloud;

/// <summary>Configuration section <c>Nextcloud</c>: the background sync (ADR-014).</summary>
public sealed class NextcloudWorkerOptions
{
    public const string SectionName = "Nextcloud";

    public static readonly TimeSpan MinimumSyncInterval = TimeSpan.FromMinutes(1);

    /// <summary>Switches the background sync off, e.g. in tests.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Runs <see cref="SyncNextcloud"/> on a <see cref="PeriodicTimer"/> while Nextcloud is connected and the credentials
/// have not been rejected (ADR-014, ADR-008). The handler notes the outcome on the connection for the status display;
/// failures are retried on the next tick. The log contains only counts and error codes.
/// </summary>
internal sealed partial class NextcloudSyncBackgroundService(
    IServiceProvider services,
    IDbContextFactory<CrmDbContext> dbFactory,
    IOptions<NextcloudWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<NextcloudSyncBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _interval = options.Value.SyncInterval < NextcloudWorkerOptions.MinimumSyncInterval
        ? NextcloudWorkerOptions.MinimumSyncInterval
        : options.Value.SyncInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, timeProvider);
        do
        {
            try
            {
                await SyncOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // E.g. the database is briefly unavailable; the next tick tries again.
                LogTickFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            // Paused after a 401 until new credentials are saved.
            if (!await db.NextcloudConnections.AnyAsync(c => !c.IsCredentialRejected, cancellationToken))
            {
                return;
            }
        }

        var handler = services.GetRequiredService<ICommandHandler<SyncNextcloud.Command, SyncNextcloud.Result>>();
        var result = await handler.Handle(new SyncNextcloud.Command(), cancellationToken);
        if (result.IsSuccess)
        {
            var r = result.Value;
            if (r.Written > 0 || r.Deleted > 0 || r.Completed > 0 || r.Reopened > 0 || r.Skipped > 0)
            {
                LogSynced(logger, r.Written, r.Deleted, r.Unchanged, r.Completed, r.Reopened, r.Skipped);
            }

            return;
        }

        LogSyncFailed(logger, result.Error.Code);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Nextcloud sync wrote {Written} and deleted {Deleted} records ({Unchanged} unchanged); took over {Completed} completed and {Reopened} reopened tasks, skipped {Skipped}")]
    private static partial void LogSynced(ILogger logger, int written, int deleted, int unchanged, int completed, int reopened, int skipped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nextcloud sync failed: {Code}")]
    private static partial void LogSyncFailed(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Nextcloud sync tick failed; retrying on the next tick")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
