namespace SoloCrm.Domain.Enrichment;

/// <summary>
/// What a suggestion proposes (SPEC 9.3). The address is one suggestion as a whole; legal form, register court and
/// number, managing directors and VAT id end up in <c>ExtraFields</c>; the company profile becomes a note.
/// </summary>
public enum EnrichmentField
{
    Address,
    Phone,
    Email,
    LegalForm,
    RegisterCourt,
    RegisterNumber,
    ManagingDirectors,
    VatId,
    CompanyProfile,
}
