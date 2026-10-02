using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;

namespace SoloCrm.Domain.Tests.Contacts;

public sealed class ContactTests
{
    [Fact]
    public void Create_ValuesWithWhitespace_TrimsValues()
    {
        var contact = Contact.Create(" Ada ", " Lovelace ", " ada@example.test ", " 123 ", " CTO ", " https://example.test ");

        contact.FirstName.Should().Be("Ada");
        contact.LastName.Should().Be("Lovelace");
        contact.Email.Should().Be("ada@example.test");
        contact.Phone.Should().Be("123");
        contact.JobTitle.Should().Be("CTO");
        contact.LinkedInUrl.Should().Be("https://example.test");
    }

    [Fact]
    public void Create_BlankOptionalValues_StoresNull()
    {
        var contact = Contact.Create("Ada", "  ", "", " ", null, "\t");

        contact.LastName.Should().BeNull();
        contact.Email.Should().BeNull();
        contact.Phone.Should().BeNull();
        contact.JobTitle.Should().BeNull();
        contact.LinkedInUrl.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", "")]
    public void Create_NoName_Throws(string? firstName, string? lastName)
    {
        var act = () => Contact.Create(firstName, lastName);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ValidContact_RaisesContactCreated()
    {
        var contact = Contact.Create(null, "Lovelace");

        contact.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new ContactCreated(contact.Id));
    }

    [Fact]
    public void Update_NewValues_ReplacesFieldsWithoutEvent()
    {
        var contact = Contact.Create("Ada", "Lovelace", "ada@example.test");
        contact.ClearDomainEvents();
        var organizationId = Guid.CreateVersion7();

        contact.Update(" Ada ", "King", null, "123", "CTO", null, organizationId, LeadSource.Referral);

        contact.FirstName.Should().Be("Ada");
        contact.LastName.Should().Be("King");
        contact.Email.Should().BeNull();
        contact.Phone.Should().Be("123");
        contact.OrganizationId.Should().Be(organizationId);
        contact.Source.Should().Be(LeadSource.Referral);
        contact.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_NoName_Throws()
    {
        var contact = Contact.Create("Ada", null);

        var act = () => contact.Update(" ", null, null, null, null, null, null, null);

        act.Should().Throw<ArgumentException>();
        contact.FirstName.Should().Be("Ada");
    }

    [Fact]
    public void Update_UnknownSource_Throws()
    {
        var contact = Contact.Create("Ada", null);

        var act = () => contact.Update("Ada", null, null, null, null, null, null, (LeadSource)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetExtraField_ValueAndBlank_SetsTrimmedValueAndRemovesIt()
    {
        var contact = Contact.Create("Ada", "Lovelace");
        var before = contact.ExtraFields;

        contact.SetExtraField("HubSpotRecordId", " 4711 ");

        contact.ExtraFields.Should().Equal(new Dictionary<string, string> { ["HubSpotRecordId"] = "4711" });
        contact.ExtraFields.Should().NotBeSameAs(before);

        contact.SetExtraField("HubSpotRecordId", " ");

        contact.ExtraFields.Should().BeEmpty();
    }

    [Fact]
    public void Erase_Always_RaisesContactDeletedWithIdOnly()
    {
        var contact = Contact.Create("Ada", "Lovelace", "ada@example.test");
        contact.ClearDomainEvents();

        contact.Erase();

        contact.DomainEvents.Should().Equal(new ContactDeleted(contact.Id));
    }
}
