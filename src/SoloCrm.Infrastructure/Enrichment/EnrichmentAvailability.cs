using SoloCrm.Application.Abstractions;

namespace SoloCrm.Infrastructure.Enrichment;

internal sealed class EnrichmentAvailability(bool isAvailable) : IEnrichmentAvailability
{
    public bool IsAvailable { get; } = isAvailable;
}
