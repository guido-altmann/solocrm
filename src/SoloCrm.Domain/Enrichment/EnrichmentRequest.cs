using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Enrichment;

/// <summary>
/// One request to enrich a record with public data (SPEC 9.3, ADR-016). The table is the queue of the
/// <c>EnrichmentProcessor</c>: it claims <see cref="EnrichmentRequestStatus.Pending"/> requests with a lease, like the
/// outbox (ADR-008). At most one request per record is active (<c>Pending</c> or <c>Running</c>), enforced by a
/// partial unique index.
/// </summary>
public sealed class EnrichmentRequest : Entity
{
    public const int ProviderMaxLength = 50;
    public const int ErrorMaxLength = 500;

    // Required by EF Core.
    private EnrichmentRequest()
    {
        Provider = null!;
    }

    private EnrichmentRequest(EnrichmentTargetType targetType, Guid targetId, string provider, DateTimeOffset requestedAt)
    {
        if (!Enum.IsDefined(targetType))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Unknown target type.");
        }

        if (targetId == Guid.Empty)
        {
            throw new ArgumentException("A target is required.", nameof(targetId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var trimmed = provider.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed.Length, ProviderMaxLength, nameof(provider));

        TargetType = targetType;
        TargetId = targetId;
        Provider = trimmed;
        RequestedAt = requestedAt.ToUniversalTime();
        Status = EnrichmentRequestStatus.Pending;
    }

    public EnrichmentTargetType TargetType { get; private set; }

    public Guid TargetId { get; private set; }

    public string Provider { get; private set; }

    public EnrichmentRequestStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>While <see cref="EnrichmentRequestStatus.Running"/>: until when the claim holds; afterwards another run may take over.</summary>
    public DateTimeOffset? LeaseUntil { get; private set; }

    /// <summary>Number of times the request was claimed.</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Why the last attempt failed; kept after a retry, cleared on success.</summary>
    public string? Error { get; private set; }

    public bool IsActive => Status is EnrichmentRequestStatus.Pending or EnrichmentRequestStatus.Running;

    public static EnrichmentRequest Create(EnrichmentTargetType targetType, Guid targetId, string provider, DateTimeOffset requestedAt) =>
        new(targetType, targetId, provider, requestedAt);

    /// <summary>
    /// Takes the request for processing. A <c>Running</c> request can only be claimed again after its lease has expired
    /// (the earlier run was aborted).
    /// </summary>
    /// <exception cref="InvalidOperationException">The request is finished or its lease is still valid.</exception>
    public void Claim(DateTimeOffset now, DateTimeOffset leaseUntil)
    {
        if (Status is EnrichmentRequestStatus.Completed or EnrichmentRequestStatus.Failed)
        {
            throw new InvalidOperationException($"A {Status} request cannot be claimed.");
        }

        if (Status == EnrichmentRequestStatus.Running && LeaseUntil > now)
        {
            throw new InvalidOperationException("The request is still being processed.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseUntil, now, nameof(leaseUntil));

        Status = EnrichmentRequestStatus.Running;
        LeaseUntil = leaseUntil.ToUniversalTime();
        Attempts++;
    }

    /// <exception cref="InvalidOperationException">The request is not running.</exception>
    public void Complete(DateTimeOffset completedAt)
    {
        EnsureRunning();
        Status = EnrichmentRequestStatus.Completed;
        CompletedAt = completedAt.ToUniversalTime();
        LeaseUntil = null;
        Error = null;
    }

    /// <summary>The attempt failed but another one follows: back to the queue.</summary>
    /// <exception cref="InvalidOperationException">The request is not running.</exception>
    public void ScheduleRetry(string error)
    {
        EnsureRunning();
        Status = EnrichmentRequestStatus.Pending;
        LeaseUntil = null;
        Error = ShortenedError(error);
    }

    /// <summary>The attempt failed and no further attempt follows.</summary>
    /// <exception cref="InvalidOperationException">The request is not running.</exception>
    public void Fail(DateTimeOffset failedAt, string error)
    {
        EnsureRunning();
        Status = EnrichmentRequestStatus.Failed;
        CompletedAt = failedAt.ToUniversalTime();
        LeaseUntil = null;
        Error = ShortenedError(error);
    }

    private void EnsureRunning()
    {
        if (Status != EnrichmentRequestStatus.Running)
        {
            throw new InvalidOperationException($"Only a running request can be finished, but it is {Status}.");
        }
    }

    private static string ShortenedError(string error)
    {
        var text = string.IsNullOrWhiteSpace(error) ? "Unbekannter Fehler." : error.Trim();
        return text.Length > ErrorMaxLength ? text[..ErrorMaxLength] : text;
    }
}
