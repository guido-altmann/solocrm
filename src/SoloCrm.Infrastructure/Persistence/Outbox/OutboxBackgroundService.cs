using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SoloCrm.Infrastructure.Persistence.Outbox;

/// <summary>
/// Runs the <see cref="OutboxProcessor"/> on a <see cref="PeriodicTimer"/> (ADR-008, iteration 5 decision 1).
/// Each tick drains all due messages batch by batch; once a day it also cleans up (decision 8).
/// </summary>
internal sealed partial class OutboxBackgroundService(
    OutboxProcessor processor,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;
    private DateTimeOffset? _lastCleanup;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollingInterval, timeProvider);
        do
        {
            try
            {
                while (await processor.ProcessDueAsync(stoppingToken) >= _options.BatchSize)
                {
                }

                var now = timeProvider.GetUtcNow();
                if (_lastCleanup is null || now - _lastCleanup >= _options.CleanupInterval)
                {
                    await processor.CleanupAsync(stoppingToken);
                    _lastCleanup = now;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // E.g. the database is briefly unavailable; the next tick tries again.
                LogTickFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox processing failed; retrying on the next tick")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
