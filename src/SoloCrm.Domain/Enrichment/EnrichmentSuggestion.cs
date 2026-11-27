using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Enrichment;

/// <summary>
/// A proposed value for one field of the enriched record (SPEC 9.3). It is never applied silently: the user accepts or
/// rejects it. An accepted suggestion is the provenance (source, time, confidence) of the applied value (SPEC 9.1).
/// </summary>
public sealed class EnrichmentSuggestion : Entity
{
    public const int ValueMaxLength = 8000;
    public const int SourceUrlMaxLength = 2000;
    public const int MaxSourceUrls = 10;

    private string[] _sourceUrls = [];

    // Required by EF Core.
    private EnrichmentSuggestion()
    {
        ProposedValue = null!;
    }

    private EnrichmentSuggestion(
        Guid requestId, EnrichmentField field, string proposedValue, string? currentValue, string[] sourceUrls, decimal confidence)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A request is required.", nameof(requestId));
        }

        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown field.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(proposedValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(proposedValue.Trim().Length, ValueMaxLength, nameof(proposedValue));

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "The confidence is between 0 and 1.");
        }

        RequestId = requestId;
        Field = field;
        ProposedValue = proposedValue.Trim();
        CurrentValue = string.IsNullOrWhiteSpace(currentValue) ? null : Shortened(currentValue.Trim());
        _sourceUrls = sourceUrls;
        Confidence = confidence;
        Status = EnrichmentSuggestionStatus.Open;
    }

    public Guid RequestId { get; private set; }

    public EnrichmentField Field { get; private set; }

    /// <summary>
    /// The suggested value as text. The address is the whole address as JSON; the company profile is Markdown.
    /// </summary>
    public string ProposedValue { get; private set; }

    /// <summary>The value of the record when the suggestion was made, <c>null</c> if the field was empty.</summary>
    public string? CurrentValue { get; private set; }

    /// <summary>Absolute <c>http</c>/<c>https</c> URLs; at least one. More than one for a company profile.</summary>
    public IReadOnlyList<string> SourceUrls => _sourceUrls;

    /// <summary>The provider's confidence, 0 to 1.</summary>
    public decimal Confidence { get; private set; }

    public EnrichmentSuggestionStatus Status { get; private set; }

    /// <summary>When the user accepted or rejected the suggestion.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    public bool IsOpen => Status == EnrichmentSuggestionStatus.Open;

    /// <exception cref="ArgumentException">No source, too many sources, or a source that is not an absolute http(s) URL.</exception>
    public static EnrichmentSuggestion Create(
        Guid requestId, EnrichmentField field, string proposedValue, string? currentValue, IEnumerable<string> sourceUrls, decimal confidence)
    {
        ArgumentNullException.ThrowIfNull(sourceUrls);

        var urls = sourceUrls.Select(u => u?.Trim() ?? "").Distinct(StringComparer.Ordinal).ToArray();
        if (urls.Length is 0 or > MaxSourceUrls)
        {
            throw new ArgumentException($"1 to {MaxSourceUrls} sources are required.", nameof(sourceUrls));
        }

        if (!urls.All(IsWebUrl))
        {
            throw new ArgumentException("A source must be an absolute http or https URL.", nameof(sourceUrls));
        }

        return new EnrichmentSuggestion(requestId, field, proposedValue, currentValue, urls, confidence);
    }

    /// <exception cref="InvalidOperationException">The suggestion is not open.</exception>
    public void Accept(DateTimeOffset decidedAt) => Decide(EnrichmentSuggestionStatus.Accepted, decidedAt);

    /// <exception cref="InvalidOperationException">The suggestion is not open.</exception>
    public void Reject(DateTimeOffset decidedAt) => Decide(EnrichmentSuggestionStatus.Rejected, decidedAt);

    /// <summary>A newer request for the same record replaces this open suggestion.</summary>
    /// <exception cref="InvalidOperationException">The suggestion is not open.</exception>
    public void Supersede()
    {
        EnsureOpen();
        Status = EnrichmentSuggestionStatus.Superseded;
    }

    private void Decide(EnrichmentSuggestionStatus status, DateTimeOffset decidedAt)
    {
        EnsureOpen();
        Status = status;
        DecidedAt = decidedAt.ToUniversalTime();
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException($"The suggestion is already {Status}.");
        }
    }

    private static bool IsWebUrl(string url) =>
        url.Length <= SourceUrlMaxLength
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    private static string Shortened(string value) => value.Length > ValueMaxLength ? value[..ValueMaxLength] : value;
}
