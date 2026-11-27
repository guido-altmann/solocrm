using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Application.Tests.Features.Enrichment;

public sealed class EnrichmentAddressTests
{
    [Fact]
    public void From_ValidParts_TrimsAndUppercasesTheCountry()
    {
        var address = EnrichmentAddress.From(" Musterstraße 1 ", " ", "10115", "Berlin", null, "de");

        address.Should().Be(new EnrichmentAddress("Musterstraße 1", null, "10115", "Berlin", null, "DE"));
    }

    [Theory]
    [InlineData(null, null, null, null, null, null)]
    [InlineData(" ", null, null, null, null, " ")]
    public void From_NothingLeft_Throws(string? street, string? street2, string? postalCode, string? city, string? region, string? country)
    {
        var act = () => EnrichmentAddress.From(street, street2, postalCode, city, region, country);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_UnknownCountryCode_Throws()
    {
        var act = () => EnrichmentAddress.From("Weg 1", null, null, null, null, "Deutschland");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToJson_ThenTryParse_RoundTripsAndOmitsEmptyParts()
    {
        var address = EnrichmentAddress.From("Musterstraße 1", null, "10115", "Berlin", null, "DE");

        var json = address.ToJson();

        json.Should().Be("""{"street":"Musterstraße 1","postalCode":"10115","city":"Berlin","countryCode":"DE"}""");
        EnrichmentAddress.TryParse(json).Should().Be(address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein json")]
    [InlineData("[1]")]
    public void TryParse_NotAnAddress_ReturnsNull(string json)
    {
        EnrichmentAddress.TryParse(json).Should().BeNull();
    }

    [Fact]
    public void ToAddress_ReturnsTheDomainAddress()
    {
        var address = EnrichmentAddress.From("Musterstraße 1", "c/o Mustermann", "10115", "Berlin", "Berlin", "DE").ToAddress();

        address.Should().Be(Address.Create("Musterstraße 1", "c/o Mustermann", "10115", "Berlin", "Berlin", "DE"));
    }
}

public sealed class EnrichmentFieldKeysTests
{
    [Theory]
    [InlineData(EnrichmentField.LegalForm, "LegalForm")]
    [InlineData(EnrichmentField.RegisterCourt, "RegisterCourt")]
    [InlineData(EnrichmentField.RegisterNumber, "RegisterNumber")]
    [InlineData(EnrichmentField.ManagingDirectors, "ManagingDirectors")]
    [InlineData(EnrichmentField.VatId, "VatId")]
    public void ExtraFieldKey_ImprintDetail_ReturnsTheFixedKey(EnrichmentField field, string key)
    {
        EnrichmentFieldKeys.ExtraFieldKey(field).Should().Be(key);
    }

    [Theory]
    [InlineData(EnrichmentField.Address)]
    [InlineData(EnrichmentField.Phone)]
    [InlineData(EnrichmentField.Email)]
    [InlineData(EnrichmentField.CompanyProfile)]
    public void ExtraFieldKey_FieldWithItsOwnPlace_ReturnsNull(EnrichmentField field)
    {
        EnrichmentFieldKeys.ExtraFieldKey(field).Should().BeNull();
    }

    [Fact]
    public void ExtraFieldKey_EveryField_HasAnOwnPlaceOrAKey()
    {
        var own = new[] { EnrichmentField.Address, EnrichmentField.Phone, EnrichmentField.Email, EnrichmentField.CompanyProfile };

        foreach (var field in Enum.GetValues<EnrichmentField>().Except(own))
        {
            EnrichmentFieldKeys.ExtraFieldKey(field).Should().NotBeNull();
        }
    }
}
