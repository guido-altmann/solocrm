using Microsoft.Extensions.Logging;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>
/// The enrichment provider backed by a Foundry agent with web search (ADR-016, points 4 and 5). Owns the answer schema,
/// validates the answer and keeps only values that name at least one source. The sources are the ones the agent states
/// in its answer: the service delivers <c>url_citation</c> annotations only for free text, not for a structured answer,
/// so they cannot be required. The user checks the source link before accepting a value (ADR-016, point 5).
/// </summary>
internal sealed partial class FoundryEnrichmentProvider(IFoundryAgent agent, TimeProvider timeProvider, ILogger<FoundryEnrichmentProvider> logger)
    : IEnrichmentProvider
{
    public const string ProviderName = "FoundryAgent";

    public string Name => ProviderName;

    public async Task<IReadOnlyList<ProposedValue>> EnrichAsync(EnrichmentSubject subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var started = timeProvider.GetTimestamp();
        var reply = await agent.AskAsync(EnrichmentSchema.BuildInput(subject), cancellationToken);
        var duration = timeProvider.GetElapsedTime(started);

        // Only ids, counts and durations: never queries, URLs or values (ADR-016, point 8).
        if (!reply.IsComplete || string.IsNullOrWhiteSpace(reply.Text))
        {
            LogIncomplete(logger, subject.TargetId, (long)duration.TotalMilliseconds, reply.SearchCount, reply.InputTokens, reply.OutputTokens);
            throw new EnrichmentProviderException("Der Agent hat keine vollständige Antwort geliefert.", isTransient: true);
        }

        ParsedAnswer parsed;
        try
        {
            parsed = EnrichmentResponseParser.Parse(reply.Text);
        }
        catch (EnrichmentProviderException)
        {
            LogInvalid(logger, subject.TargetId, (long)duration.TotalMilliseconds, reply.SearchCount, reply.InputTokens, reply.OutputTokens);
            throw;
        }

        // Citations of the service, if any, only tell how many values are confirmed; they are not required.
        var cited = reply.CitedUrls.Select(NormalizeUrl).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var values = new List<ProposedValue>();
        var withoutSource = 0;
        var confirmed = 0;
        foreach (var suggestion in parsed.Suggestions)
        {
            var sources = suggestion.SourceUrls
                .Select(url => url.Trim())
                .Where(url => NormalizeUrl(url) is not null)
                .Distinct(StringComparer.Ordinal)
                .Take(EnrichmentSuggestion.MaxSourceUrls)
                .ToList();
            if (sources.Count == 0)
            {
                withoutSource++;
                continue;
            }

            if (sources.Any(url => cited.Contains(NormalizeUrl(url)!)))
            {
                confirmed++;
            }

            values.Add(new ProposedValue(suggestion.Field, suggestion.Value, sources, suggestion.Confidence));
        }

        LogFinished(
            logger, subject.TargetId, (long)duration.TotalMilliseconds, values.Count, parsed.Discarded, withoutSource, confirmed, reply.SearchCount, reply.InputTokens, reply.OutputTokens);
        return values;
    }

    /// <summary>
    /// Scheme and host in lower case, default port, fragment and trailing slash dropped; <c>null</c> if it is not an
    /// absolute <c>http</c>/<c>https</c> URL.
    /// </summary>
    internal static string? NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}{uri.Query}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Enrichment of {TargetId} got no complete answer after {DurationMs} ms ({Searches} searches, {InputTokens}/{OutputTokens} tokens)")]
    private static partial void LogIncomplete(ILogger logger, Guid targetId, long durationMs, int searches, long? inputTokens, long? outputTokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Enrichment of {TargetId} got an invalid answer after {DurationMs} ms ({Searches} searches, {InputTokens}/{OutputTokens} tokens)")]
    private static partial void LogInvalid(ILogger logger, Guid targetId, long durationMs, int searches, long? inputTokens, long? outputTokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enrichment of {TargetId} finished in {DurationMs} ms: {Suggestions} suggestions, {Discarded} discarded, {WithoutSource} without source, {Confirmed} confirmed by citations, {Searches} searches, {InputTokens}/{OutputTokens} tokens")]
    private static partial void LogFinished(ILogger logger, Guid targetId, long durationMs, int suggestions, int discarded, int withoutSource, int confirmed, int searches, long? inputTokens, long? outputTokens);
}
