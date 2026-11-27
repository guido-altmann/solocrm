using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Application.Features.Enrichment;

/// <summary>Business errors of <see cref="AcceptSuggestion"/> and <see cref="RejectSuggestion"/>.</summary>
public static class EnrichmentSuggestionErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Enrichment.SuggestionNotFound", "Der Vorschlag wurde nicht gefunden.");

    public static Error Archived { get; } =
        Error.Conflict("Enrichment.Archived", "Die Organisation ist archiviert. Bitte zuerst wiederherstellen.");

    public static Error InvalidValue { get; } =
        Error.Failure("Enrichment.InvalidValue", "Der Vorschlag enthält keinen gültigen Wert und kann nicht übernommen werden.");

    /// <summary>The suggestion was decided or replaced already.</summary>
    public static Error NotOpen(EnrichmentSuggestionStatus status) => status switch
    {
        EnrichmentSuggestionStatus.Superseded =>
            Error.Conflict("Enrichment.SuggestionSuperseded", "Der Vorschlag ist überholt. Bitte die aktuellen Vorschläge laden."),
        _ =>
            Error.Conflict("Enrichment.SuggestionDecided", "Über diesen Vorschlag wurde bereits entschieden."),
    };
}
