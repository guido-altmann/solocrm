using SoloCrm.Domain.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Domain.Tests.Organizations;

public sealed class OrganizationTests
{
    [Fact]
    public void Create_NameOnly_DefaultsToTypeOtherAndRaisesEvent()
    {
        var organization = Organization.Create(" Contoso ");

        organization.Name.Should().Be("Contoso");
        organization.Type.Should().Be(OrganizationType.Other);
        organization.Website.Should().BeNull();
        organization.ExtraFields.Should().BeEmpty();
        organization.DomainEvents.Should().ContainSingle().Which.Should().Be(new OrganizationCreated(organization.Id));
    }

    [Fact]
    public void Create_AllValues_NormalizesWebsiteAndTrims()
    {
        var organization = Organization.Create("Contoso", OrganizationType.Client, "Contoso.de/", Address.Create(city: " Berlin "), " ");

        organization.Type.Should().Be(OrganizationType.Client);
        organization.Website.Should().Be("https://contoso.de");
        organization.Address.City.Should().Be("Berlin");
        organization.Notes.Should().BeNull();
    }

    [Fact]
    public void Create_PhoneAndEmail_TrimsAndStoresBlankAsNull()
    {
        var organization = Organization.Create("Contoso", phone: " +49 30 123456 ", email: " info@contoso.de ");
        var blank = Organization.Create("Fabrikam", phone: " ", email: "");

        organization.Phone.Should().Be("+49 30 123456");
        organization.Email.Should().Be("info@contoso.de");
        blank.Phone.Should().BeNull();
        blank.Email.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankName_Throws(string name)
    {
        var act = () => Organization.Create(name);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_InvalidWebsite_Throws()
    {
        var act = () => Organization.Create("Contoso", website: "not a url");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_NewValues_ReplacesFieldsWithoutEvent()
    {
        var organization = Organization.Create("Contoso");
        organization.ClearDomainEvents();

        organization.Update("Contoso AG", OrganizationType.Agency, null, Address.Create(city: "Köln"), "Notiz", "0221 98765", "kontakt@contoso.de");

        organization.Name.Should().Be("Contoso AG");
        organization.Type.Should().Be(OrganizationType.Agency);
        organization.Address.City.Should().Be("Köln");
        organization.Notes.Should().Be("Notiz");
        organization.Phone.Should().Be("0221 98765");
        organization.Email.Should().Be("kontakt@contoso.de");
        organization.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_UnknownType_Throws()
    {
        var organization = Organization.Create("Contoso");

        var act = () => organization.Update("Contoso", (OrganizationType)99, null, null, null, null, null);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
