using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Application.Abstractions;

/// <summary>
/// Finds public data about a company (ADR-016, point 2). Implemented per provider in Infrastructure; the first and
/// only one is a Foundry agent with web search. A provider only proposes values: it neither reads nor changes CRM data.
/// </summary>
public interface IEnrichmentProvider
{
    /// <summary>Stable name of the provider, stored with every request (e.g. <c>FoundryAgent</c>).</summary>
    string Name { get; }

    /// <summary>Returns the verified suggestions for <paramref name="subject"/>; possibly none.</summary>
    /// <exception cref="EnrichmentProviderException">The provider could not deliver a usable answer.</exception>
    Task<IReadOnlyList<ProposedValue>> EnrichAsync(EnrichmentSubject subject, CancellationToken cancellationToken);
}

/// <summary>
/// What a provider may know about the record (data minimization, ADR-016 point 8): the name and, if there is one, the
/// domain of the website. No notes, contacts or other CRM data.
/// </summary>
/// <param name="TargetId">Only for correlating log entries.</param>
/// <param name="Domain">Host name without <c>www.</c>, e.g. <c>example.de</c>.</param>
public sealed record EnrichmentSubject(Guid TargetId, string Name, string? Domain);

/// <summary>
/// One verified value. <paramref name="Value"/> is plain text, except for <see cref="EnrichmentField.Address"/> (JSON of
/// <c>EnrichmentAddress</c>) and <see cref="EnrichmentField.CompanyProfile"/> (Markdown).
/// </summary>
/// <param name="SourceUrls">Absolute <c>http</c>/<c>https</c> URLs, as named by the provider's answer; not verified by the app.</param>
/// <param name="Confidence">0 to 1.</param>
public sealed record ProposedValue(EnrichmentField Field, string Value, IReadOnlyList<string> SourceUrls, decimal Confidence);

/// <summary>
/// The provider could not deliver a usable answer. <see cref="Exception.Message"/> is shown to the user and therefore
/// German; it never contains values or URLs from the answer.
/// </summary>
public sealed class EnrichmentProviderException : Exception
{
    public EnrichmentProviderException(string message, bool isTransient = false, Exception? innerException = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
    }

    /// <summary>A retry may help (timeout, overload, network); otherwise it is pointless (denied, invalid answer).</summary>
    public bool IsTransient { get; }
}
