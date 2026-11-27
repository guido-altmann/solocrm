using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Domain.Enrichment;
using SoloCrm.Domain.Organizations;
using SoloCrm.Infrastructure.Persistence;

namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>
/// Works off the enrichment queue (ADR-016, point 3), following the outbox pattern (ADR-008):
/// <list type="number">
/// <item>Claim: one statement picks a <c>Pending</c> request, or a <c>Running</c> one whose lease has expired, with
/// <c>FOR UPDATE SKIP LOCKED</c> and sets the lease. A second processor skips it, and no transaction stays open during
/// the agent call.</item>
/// <item>Run: the provider is called with a timeout.</item>
/// <item>Finish: the suggestions are stored, older open ones of the same organization become <c>Superseded</c> and the
/// request is <c>Completed</c> in one transaction. A transient failure puts the request back in the queue until
/// <see cref="EnrichmentProcessingOptions.MaxAttempts"/> is reached; every other failure ends it as <c>Failed</c>.</item>
/// </list>
/// The claim sets what <see cref="EnrichmentRequest.Claim"/> describes (status, lease, attempts) in SQL.
/// </summary>
public sealed partial class EnrichmentProcessor(
    IDbContextFactory<CrmDbContext> dbFactory,
    IEnrichmentProvider provider,
    TimeProvider timeProvider,
    IOptions<EnrichmentProcessingOptions> options,
    ILogger<EnrichmentProcessor> logger)
{
    private readonly EnrichmentProcessingOptions _options = options.Value;

    /// <summary>Processes the next due request, if there is one, and returns how many were claimed (0 or 1).</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        await FailInterruptedAsync(cancellationToken);

        var claimed = await ClaimAsync(cancellationToken);
        foreach (var requestId in claimed)
        {
            try
            {
                await ProcessAsync(requestId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The lease expires and the request is picked up again, or ends as Failed after the last attempt.
                LogProcessingFailed(logger, ex, requestId);
            }
        }

        return claimed.Count;
    }

    /// <summary>Requests whose processing was interrupted on every attempt (crash, restart) end as <c>Failed</c>.</summary>
    private async Task FailInterruptedAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var maxAttempts = _options.MaxAttempts;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var failed = await db.Database.ExecuteSqlAsync($"""
            UPDATE enrichment_requests
            SET status = 'Failed', lease_until = NULL, completed_at = {now}, updated_at = {now},
                error = 'Die Verarbeitung wurde mehrfach unterbrochen.'
            WHERE status = 'Running' AND lease_until <= {now} AND attempts >= {maxAttempts}
            """, cancellationToken);
        if (failed > 0)
        {
            LogInterrupted(logger, failed);
        }
    }

    private async Task<List<Guid>> ClaimAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + _options.LeaseDuration;
        var maxAttempts = _options.MaxAttempts;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE enrichment_requests
            SET status = 'Running', lease_until = {leaseUntil}, attempts = attempts + 1, updated_at = {now}
            WHERE id IN (
                SELECT id FROM enrichment_requests
                WHERE (status = 'Pending' OR (status = 'Running' AND lease_until <= {now})) AND attempts < {maxAttempts}
                ORDER BY requested_at, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """).ToListAsync(cancellationToken);
    }

    private async Task ProcessAsync(Guid requestId, CancellationToken stoppingToken)
    {
        EnrichmentSubject subject;
        await using (var db = await dbFactory.CreateDbContextAsync(stoppingToken))
        {
            var targetId = await db.EnrichmentRequests.Where(r => r.Id == requestId).Select(r => r.TargetId).SingleAsync(stoppingToken);
            var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == targetId, stoppingToken);
            if (organization is null or { IsArchived: true })
            {
                await FinishFailedAsync(requestId, "Die Organisation existiert nicht mehr oder ist archiviert.", isTransient: false, stoppingToken);
                return;
            }

            subject = EnrichmentSubjects.ForOrganization(organization);
        }

        using var timeout = new CancellationTokenSource(_options.RunTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
        IReadOnlyList<ProposedValue> values;
        try
        {
            values = await provider.EnrichAsync(subject, linked.Token);
        }
        catch (EnrichmentProviderException ex)
        {
            await FinishFailedAsync(requestId, ex.Message, ex.IsTransient, stoppingToken);
            return;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            await FinishFailedAsync(requestId, "Der Agent hat nicht rechtzeitig geantwortet.", isTransient: true, stoppingToken);
            return;
        }

        await FinishCompletedAsync(requestId, values, stoppingToken);
    }

    private async Task FinishFailedAsync(Guid requestId, string message, bool isTransient, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.EnrichmentRequests.SingleAsync(r => r.Id == requestId, cancellationToken);
        if (request.Status != EnrichmentRequestStatus.Running)
        {
            return;
        }

        var retry = isTransient && request.Attempts < _options.MaxAttempts;
        if (retry)
        {
            request.ScheduleRetry(message);
        }
        else
        {
            request.Fail(timeProvider.GetUtcNow(), message);
        }

        await db.SaveChangesAsync(cancellationToken);
        LogFailed(logger, requestId, request.Attempts, retry);
    }

    private async Task FinishCompletedAsync(Guid requestId, IReadOnlyList<ProposedValue> values, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var request = await db.EnrichmentRequests.SingleAsync(r => r.Id == requestId, cancellationToken);
        if (request.Status != EnrichmentRequestStatus.Running)
        {
            // Another processor took over after the lease had expired and finished first.
            LogDiscarded(logger, requestId);
            return;
        }

        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == request.TargetId, cancellationToken);
        if (organization is null)
        {
            request.Fail(timeProvider.GetUtcNow(), "Die Organisation existiert nicht mehr.");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var targetType = request.TargetType;
        var targetId = request.TargetId;
        var older = await db.EnrichmentSuggestions
            .Where(s => s.Status == EnrichmentSuggestionStatus.Open
                && db.EnrichmentRequests.Any(r => r.Id == s.RequestId && r.Id != requestId && r.TargetType == targetType && r.TargetId == targetId))
            .ToListAsync(cancellationToken);
        foreach (var suggestion in older)
        {
            suggestion.Supersede();
        }

        var stored = 0;
        foreach (var value in values)
        {
            try
            {
                db.EnrichmentSuggestions.Add(EnrichmentSuggestion.Create(
                    requestId, value.Field, value.Value, CurrentValue(organization, value.Field), value.SourceUrls, value.Confidence));
                stored++;
            }
            catch (ArgumentException)
            {
                // Not storable (e.g. a source URL that is too long); the rest is still worth keeping.
            }
        }

        request.Complete(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        LogCompleted(logger, requestId, stored, values.Count - stored, older.Count);
    }

    /// <summary>The value the field has in the CRM now, in the format of the suggestion; <c>null</c> if empty.</summary>
    private static string? CurrentValue(Organization organization, EnrichmentField field) => field switch
    {
        EnrichmentField.Address => EnrichmentAddress.FromAddress(organization.Address)?.ToJson(),
        EnrichmentField.Phone => organization.Phone,
        EnrichmentField.Email => organization.Email,
        EnrichmentField.CompanyProfile => null,
        _ => EnrichmentFieldKeys.ExtraFieldKey(field) is { } key && organization.ExtraFields.TryGetValue(key, out var value) ? value : null,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Enrichment request {RequestId} could not be processed; retrying after the lease")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception, Guid requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} enrichment request(s) failed because the processing was interrupted on every attempt")]
    private static partial void LogInterrupted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enrichment request {RequestId} failed on attempt {Attempts}; will retry: {WillRetry}")]
    private static partial void LogFailed(ILogger logger, Guid requestId, int attempts, bool willRetry);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enrichment request {RequestId} completed with {Stored} suggestions ({Skipped} not storable, {Superseded} older ones superseded)")]
    private static partial void LogCompleted(ILogger logger, Guid requestId, int stored, int skipped, int superseded);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Enrichment request {RequestId} was finished by another processor; result discarded")]
    private static partial void LogDiscarded(ILogger logger, Guid requestId);
}
