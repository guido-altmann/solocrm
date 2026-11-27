using SoloCrm.Application.Abstractions;

namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>Used while <c>Ai</c> is not configured; the UI disables the enrichment, so this is only a safety net.</summary>
internal sealed class NotConfiguredEnrichmentProvider : IEnrichmentProvider
{
    public string Name => FoundryEnrichmentProvider.ProviderName;

    public Task<IReadOnlyList<ProposedValue>> EnrichAsync(EnrichmentSubject subject, CancellationToken cancellationToken) =>
        throw new EnrichmentProviderException("Die Anreicherung ist nicht eingerichtet (Ai__ProjectEndpoint, Ai__AgentName).");
}
