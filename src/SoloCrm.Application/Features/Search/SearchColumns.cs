namespace SoloCrm.Application.Features.Search;

/// <summary>
/// Shadow properties for the search (ADR-007). The database generates them, so the domain model stays free of
/// search concerns and EF never writes them. Queries reach them via <c>EF.Property</c>.
/// </summary>
public static class SearchColumns
{
    /// <summary>Generated <c>tsvector</c> (configuration <c>simple</c>) on contacts, organizations and opportunities.</summary>
    public const string Vector = "SearchVector";

    /// <summary>Generated full name of a contact („Vorname Nachname“), indexed for trigram similarity.</summary>
    public const string Name = "SearchName";
}
