using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Tests.Features.Enrichment;

public sealed class EnrichmentSubjectsTests
{
    [Theory]
    [InlineData("https://www.Beispiel.de/", "beispiel.de")]
    [InlineData("beispiel.de/kontakt", "beispiel.de")]
    [InlineData("http://shop.beispiel.de:8080/x?y=1", "shop.beispiel.de")]
    public void ForOrganization_WithWebsite_UsesTheHostWithoutWww(string website, string domain)
    {
        var organization = Organization.Create("Beispiel GmbH", website: website);

        var subject = EnrichmentSubjects.ForOrganization(organization);

        subject.Domain.Should().Be(domain);
        subject.Name.Should().Be("Beispiel GmbH");
        subject.TargetId.Should().Be(organization.Id);
    }

    [Fact]
    public void ForOrganization_NoWebsite_HasNoDomain()
    {
        EnrichmentSubjects.ForOrganization(Organization.Create("Beispiel GmbH")).Domain.Should().BeNull();
    }

    [Fact]
    public void ForOrganization_CarriesNothingButIdNameAndDomain()
    {
        var organization = Organization.Create(
            "Beispiel GmbH", website: "beispiel.de", notes: "vertraulich", phone: "030 1", email: "a@b.test",
            address: Address.Create(city: "Berlin"));

        var subject = EnrichmentSubjects.ForOrganization(organization);

        subject.ToString().Should().NotContain("vertraulich").And.NotContain("030 1").And.NotContain("a@b.test").And.NotContain("Berlin");
    }
}
