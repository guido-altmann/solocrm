using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Webhooks;
using SoloCrm.Domain.Webhooks;

namespace SoloCrm.Infrastructure.Persistence.Outbox;

/// <summary>
/// Delivers due outbox messages to the webhook subscriptions (ADR-008, ADR-010).
/// <list type="number">
/// <item>Claim: one statement selects due messages with <c>FOR UPDATE SKIP LOCKED</c> and pushes their
/// <c>next_attempt_at</c> by the lease. A second processor (rolling update, ADR-009) skips and no longer sees them,
/// and no transaction stays open during HTTP calls.</item>
/// <item>Deliver: each receiving subscription without a successful delivery gets one attempt; every attempt is logged.</item>
/// <item>Finish: processed when nothing is left to retry, otherwise rescheduled per <see cref="WebhookRetryPolicy"/>.</item>
/// </list>
/// </summary>
public sealed partial class OutboxProcessor(
    IDbContextFactory<CrmDbContext> dbFactory,
    IWebhookSender sender,
    ISecretProtector protector,
    TimeProvider timeProvider,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    private readonly OutboxOptions _options = options.Value;

    /// <summary>Processes one batch of due messages and returns how many were claimed.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimAsync(cancellationToken);
        foreach (var messageId in claimed)
        {
            try
            {
                await ProcessAsync(messageId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The lease expires and the message is picked up again.
                LogProcessingFailed(logger, ex, messageId);
            }
        }

        return claimed.Count;
    }

    /// <summary>Deletes processed messages and delivery log entries older than the retention (decision 8).</summary>
    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - _options.Retention;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var deliveries = await db.WebhookDeliveries
            .Where(d => d.AttemptedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (messages + deliveries > 0)
        {
            LogCleanedUp(logger, messages, deliveries);
        }
    }

    private async Task<List<Guid>> ClaimAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + _options.LeaseDuration;
        var batchSize = _options.BatchSize;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE outbox_messages
            SET next_attempt_at = {leaseUntil}
            WHERE id IN (
                SELECT id FROM outbox_messages
                WHERE processed_at IS NULL AND next_attempt_at <= {now}
                ORDER BY next_attempt_at, id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """).ToListAsync(cancellationToken);
    }

    private async Task ProcessAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var message = await db.OutboxMessages.SingleAsync(m => m.Id == messageId, cancellationToken);
        if (message.ProcessedAt is not null)
        {
            return;
        }

        // Decision 3: only subscriptions that existed when the event occurred.
        var subscriptions = await db.WebhookSubscriptions
            .Where(s => s.IsActive && s.CreatedAt <= message.OccurredAt && s.Events.Contains(message.Type))
            .ToListAsync(cancellationToken);

        var previous = await db.WebhookDeliveries
            .Where(d => d.EventId == message.Id)
            .GroupBy(d => d.SubscriptionId)
            .Select(g => new { SubscriptionId = g.Key, Attempts = g.Count(), Succeeded = g.Any(d => d.Succeeded) })
            .ToDictionaryAsync(x => x.SubscriptionId, cancellationToken);

        var pending = subscriptions
            .Where(s => !previous.TryGetValue(s.Id, out var p) || (!p.Succeeded && p.Attempts < WebhookRetryPolicy.MaxAttempts))
            .ToList();

        var now = timeProvider.GetUtcNow();
        if (pending.Count == 0)
        {
            message.MarkSkipped(now);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var webhookEvent = new WebhookEvent(message.Id, message.Type, message.OccurredAt, message.Payload);
        var failed = new List<(int Attempt, string Error)>();
        foreach (var subscription in pending)
        {
            var attempt = (previous.TryGetValue(subscription.Id, out var p) ? p.Attempts : 0) + 1;
            var sent = await SendAsync(subscription, webhookEvent, cancellationToken);
            db.WebhookDeliveries.Add(WebhookDelivery.Record(
                subscription.Id, message.Id, message.Type, attempt, sent.StatusCode, sent.DurationMs, sent.Error, timeProvider.GetUtcNow()));

            if (!sent.Succeeded)
            {
                failed.Add((attempt, $"{subscription.Name}: {sent.Error}"));
                if (attempt >= WebhookRetryPolicy.MaxAttempts)
                {
                    LogGivenUp(logger, message.Type, message.Id, subscription.Id, attempt);
                }
            }
        }

        var retriable = failed.Where(f => f.Attempt < WebhookRetryPolicy.MaxAttempts).ToList();
        var lastError = failed.Count == 0 ? null : string.Join("; ", failed.Select(f => f.Error));
        if (retriable.Count == 0)
        {
            message.MarkProcessed(now, lastError);
        }
        else
        {
            message.ScheduleRetry(WebhookRetryPolicy.NextAttemptAt(retriable.Min(f => f.Attempt), now)!.Value, lastError!);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<WebhookSendResult> SendAsync(WebhookSubscription subscription, WebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        string secret;
        try
        {
            secret = protector.Unprotect(subscription.ProtectedSecret);
        }
        catch (CryptographicException)
        {
            // Key ring lost (ADR-009): the secret must be regenerated in the settings.
            return new WebhookSendResult(null, 0, "Secret nicht lesbar – bitte neu erzeugen");
        }

        return await sender.SendAsync(subscription.Url, secret, webhookEvent, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} could not be processed; retrying after the lease")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {EventType} {WebhookEventId} given up for subscription {SubscriptionId} after {Attempts} attempts")]
    private static partial void LogGivenUp(ILogger logger, string eventType, Guid webhookEventId, Guid subscriptionId, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox cleanup: deleted {Messages} message(s) and {Deliveries} delivery log entries")]
    private static partial void LogCleanedUp(ILogger logger, int messages, int deliveries);
}
