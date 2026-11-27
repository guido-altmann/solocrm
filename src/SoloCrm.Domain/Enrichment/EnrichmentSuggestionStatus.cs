namespace SoloCrm.Domain.Enrichment;

public enum EnrichmentSuggestionStatus
{
    Open,
    Accepted,
    Rejected,

    /// <summary>Replaced by the suggestions of a newer request for the same record (SPEC 9.3).</summary>
    Superseded,
}
