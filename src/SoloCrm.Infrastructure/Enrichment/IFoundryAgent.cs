namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>
/// One call of the Foundry agent. A thin seam over the SDK so that <see cref="FoundryEnrichmentProvider"/> is testable
/// with a fake.
/// </summary>
internal interface IFoundryAgent
{
    /// <exception cref="SoloCrm.Application.Abstractions.EnrichmentProviderException">The call failed.</exception>
    Task<AgentReply> AskAsync(string input, CancellationToken cancellationToken);
}

/// <param name="Text">The text of the answer, <c>null</c> if there is none.</param>
/// <param name="CitedUrls">The URLs of the <c>url_citation</c> annotations of the answer.</param>
/// <param name="SearchCount">Number of web searches the agent made.</param>
/// <param name="IsComplete">The agent finished (not cut off, e.g. by the token limit).</param>
internal sealed record AgentReply(
    string? Text, IReadOnlyList<string> CitedUrls, int SearchCount, long? InputTokens, long? OutputTokens, bool IsComplete);
