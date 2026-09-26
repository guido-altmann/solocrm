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
}
