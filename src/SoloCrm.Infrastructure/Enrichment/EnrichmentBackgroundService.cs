using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>
/// Runs the <see cref="EnrichmentProcessor"/> on a <see cref="PeriodicTimer"/> (ADR-016, point 3). Each tick drains the
/// queue, one request at a time.
/// </summary>
internal sealed partial class EnrichmentBackgroundService(
    EnrichmentProcessor processor,
    IOptions<EnrichmentProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<EnrichmentBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval, timeProvider);
        do
        {
            try
            {
                while (await processor.ProcessDueAsync(stoppingToken) > 0)
                {
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Enrichment processing failed; retrying on the next tick")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
