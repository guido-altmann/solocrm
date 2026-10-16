using SoloCrm.Application.Features.Common;
using SoloCrm.Application.Features.Nextcloud;

namespace SoloCrm.Application.Tests.Features.Nextcloud;

public sealed class ContactVCardTests
{
    private static readonly Guid Id = Guid.Parse("0199f0a0-0000-7000-8000-000000000001");

    private static ContactVCard.Card Card(
        string? firstName = "Jörg",
        string? lastName = "Müller",
        string? email = "joerg@example.test",
        string? phone = "+49 30 1234",
        string? jobTitle = "CTO",
        string? linkedIn = "https://www.linkedin.com/in/joerg",
        string? organization = "ACME, Inc; Berlin",
        AddressData? address = null) =>
        new(Id, firstName, lastName, email, phone, jobTitle, linkedIn, organization, address ?? AddressData.Empty);

    private static string[] Lines(ContactVCard.Card card) =>
        ContactVCard.Render(card).Body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Render_AllFields_WritesVCard30WithMappedProperties()
    {
        var lines = Lines(Card(address: new AddressData("Hauptstraße 1", "c/o Büro", "10115", "Berlin", null, "DE")));

        lines.Should().StartWith(["BEGIN:VCARD", "VERSION:3.0"]).And.EndWith("END:VCARD");
        lines.Should().Contain([
            $"UID:{Id}@solocrm",
            "FN:Jörg Müller",
            "N:Müller;Jörg;;;",
            "EMAIL;TYPE=INTERNET:joerg@example.test",
            "TEL;TYPE=WORK,VOICE:+49 30 1234",
            @"ORG:ACME\, Inc\; Berlin",
            "TITLE:CTO",
            "URL:https://www.linkedin.com/in/joerg",
            "ADR;TYPE=WORK:;c/o Büro;Hauptstraße 1;Berlin;;10115;Deutschland",
        ]);
    }

    [Fact]
    public void Render_Always_UsesVCardMediaTypeAndCrLf()
    {
        var content = ContactVCard.Render(Card());

        content.MediaType.Should().Be("text/vcard; charset=utf-8");
        content.Body.Replace("\r\n", "", StringComparison.Ordinal).Should().NotContain("\n");
    }

    [Fact]
    public void Render_OnlyLastName_LeavesOptionalPropertiesOut()
    {
        var lines = Lines(Card(firstName: null, email: null, phone: null, jobTitle: null, linkedIn: null, organization: null));

        lines.Should().Contain(["FN:Müller", "N:Müller;;;;"]);
        lines.Should().NotContain(l => l.StartsWith("EMAIL", StringComparison.Ordinal)
            || l.StartsWith("TEL", StringComparison.Ordinal)
            || l.StartsWith("ORG", StringComparison.Ordinal)
            || l.StartsWith("TITLE", StringComparison.Ordinal)
            || l.StartsWith("URL", StringComparison.Ordinal)
            || l.StartsWith("ADR", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_SameContactTwice_IsByteIdenticalWithoutTimestamps()
    {
        var first = ContactVCard.Render(Card()).Body;
        var second = ContactVCard.Render(Card()).Body;

        second.Should().Be(first);
        first.Should().NotContain("REV:").And.NotContain("PRODID");
        PayloadHash.Of(second).Should().Be(PayloadHash.Of(first));
    }

    [Fact]
    public void Render_OrganizationRenamed_ChangesTheHash()
    {
        var before = PayloadHash.Of(ContactVCard.Render(Card(organization: "ACME")).Body);
        var after = PayloadHash.Of(ContactVCard.Render(Card(organization: "ACME Holding")).Body);

        after.Should().NotBe(before);
    }

    [Fact]
    public void Render_LongValue_IsFoldedTo75Octets()
    {
        var lines = ContactVCard.Render(Card(jobTitle: string.Concat(Enumerable.Repeat("Geschäftsführung ", 10)))).Body.Split("\r\n");

        lines.Should().OnlyContain(l => System.Text.Encoding.UTF8.GetByteCount(l) <= 75);
        lines.Should().Contain(l => l.StartsWith(' '));
    }

    [Fact]
    public void ResourceName_Id_IsVcfFile()
    {
        ContactVCard.ResourceName(Id).Should().Be($"{Id}.vcf");
    }
}
