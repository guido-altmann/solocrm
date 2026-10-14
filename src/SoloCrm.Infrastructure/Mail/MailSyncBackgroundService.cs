using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Mail;

namespace SoloCrm.Infrastructure.Mail;

/// <summary>
/// Runs <see cref="SyncMail"/> on a <see cref="PeriodicTimer"/> while a mailbox is connected (ADR-013, ADR-008).
/// Failures are noted on the connection for the status display and retried on the next tick; the log contains only
/// counts and error codes.
/// </summary>
internal sealed partial class MailSyncBackgroundService(
    IServiceProvider services,
    IMailConnector connector,
    IOptions<MailWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<MailSyncBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _interval = options.Value.PollingInterval < MailWorkerOptions.MinimumPollingInterval
        ? MailWorkerOptions.MinimumPollingInterval
        : options.Value.PollingInterval;

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
        if (await connector.GetConnectionAsync(cancellationToken) is null)
        {
            return;
        }

        var handler = services.GetRequiredService<ICommandHandler<SyncMail.Command, SyncMail.Result>>();
        var result = await handler.Handle(new SyncMail.Command(), cancellationToken);
        if (result.IsSuccess)
        {
            await connector.RecordSyncAsync(null, cancellationToken);
            if (result.Value.Created > 0)
            {
                LogSynced(logger, result.Value.Created, result.Value.Duplicates, result.Value.Unmatched);
            }

            return;
        }

        await connector.RecordSyncAsync(result.Error.Message, cancellationToken);
        LogSyncFailed(logger, result.Error.Code);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Mail sync logged {Created} emails ({Duplicates} duplicates, {Unmatched} without known contact)")]
    private static partial void LogSynced(ILogger logger, int created, int duplicates, int unmatched);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mail sync failed: {Code}")]
    private static partial void LogSyncFailed(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mail sync tick failed; retrying on the next tick")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
