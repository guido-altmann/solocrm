namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>Configuration section <c>Enrichment</c>: the background processing (ADR-016, point 3).</summary>
public sealed class EnrichmentProcessingOptions
{
    public const string SectionName = "Enrichment";

    /// <summary>Switches the background processing off, e.g. in tests.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Longest time of one agent call; a web search can take a while.</summary>
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long a claimed request stays invisible to other processors. Must exceed <see cref="RunTimeout"/>; after a
    /// crash the request is picked up again once the lease has expired.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Claims per request, including takeovers after an expired lease; then it is <c>Failed</c>.</summary>
    public int MaxAttempts { get; set; } = 3;
}
