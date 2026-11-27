using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Enrichment;

public static class EnrichmentSubjects
{
    /// <summary>The name and the domain of <see cref="Organization.Website"/>; nothing else leaves the CRM.</summary>
    public static EnrichmentSubject ForOrganization(Organization organization)
    {
        ArgumentNullException.ThrowIfNull(organization);
        return new EnrichmentSubject(organization.Id, organization.Name, DomainOf(organization.Website));
    }

    private static string? DomainOf(string? website)
    {
        if (!Uri.TryCreate(website, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
