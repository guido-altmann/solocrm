namespace SoloCrm.Application.Abstractions;

/// <summary>
/// Whether the AI agent for data enrichment is set up (ADR-016, point 6). Without configuration the enrichment is
/// disabled in the UI instead of failing at startup.
/// </summary>
public interface IEnrichmentAvailability
{
    bool IsAvailable { get; }
}
