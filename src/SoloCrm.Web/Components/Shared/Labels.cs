using SoloCrm.Domain.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// German display names for domain enums (the UI is German, identifiers are English).
/// </summary>
public static class Labels
{
    public static string For(OrganizationType type) => type switch
    {
        OrganizationType.Client => "Endkunde",
        OrganizationType.Agency => "Vermittler",
        OrganizationType.Partner => "Partner",
        _ => "Sonstige",
    };

    public static string For(LeadSource source) => source switch
    {
        LeadSource.LinkedIn => "LinkedIn",
        LeadSource.ProjectPortal => "Projektportal",
        LeadSource.Referral => "Empfehlung",
        LeadSource.Website => "Website",
        LeadSource.Event => "Event",
        _ => "Sonstige",
    };
}
