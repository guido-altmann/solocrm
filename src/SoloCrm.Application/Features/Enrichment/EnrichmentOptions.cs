namespace SoloCrm.Application.Features.Enrichment;

/// <summary>Configuration section <c>Enrichment</c> (ADR-016, point 9).</summary>
public sealed class EnrichmentOptions
{
    public const string SectionName = "Enrichment";

    /// <summary>
    /// Cost cap: at most this many requests are accepted per day (in the configured time zone), failed ones included.
    /// </summary>
    public int MaxRequestsPerDay { get; set; } = 20;
}
