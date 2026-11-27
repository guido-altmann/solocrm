namespace SoloCrm.Domain.Enrichment;

public enum EnrichmentRequestStatus
{
    /// <summary>Waiting in the queue (also after a failed attempt that will be retried).</summary>
    Pending,

    /// <summary>Claimed by the processor; see <see cref="EnrichmentRequest.LeaseUntil"/>.</summary>
    Running,

    Completed,

    /// <summary>Given up after the last attempt; <see cref="EnrichmentRequest.Error"/> tells why.</summary>
    Failed,
}
