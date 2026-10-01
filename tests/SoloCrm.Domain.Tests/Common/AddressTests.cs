using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Tests.Common;

public sealed class AddressTests
{
    [Fact]
    public void Create_AllValues_TrimsAndUppercasesCountry()
    {
        var address = Address.Create(" Hauptstr. 1 ", " c/o Contoso ", " 10115 ", " Berlin ", " Berlin ", " de ");

        address.Should().Be(Address.Create("Hauptstr. 1", "c/o Contoso", "10115", "Berlin", "Berlin", "DE"));
        address.CountryCode.Should().Be("DE");
        address.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Create_OnlyBlanks_ReturnsEmpty()
    {
        var address = Address.Create(" ", "", null, "  ", null, " ");

        address.Should().BeSameAs(Address.Empty);
        address.IsEmpty.Should().BeTrue();
        address.ToLines().Should().BeEmpty();
    }

    [Fact]
    public void Create_UnknownCountry_Throws()
    {
        var act = () => Address.Create(city: "Berlin", countryCode: "XX");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_TooLongValue_Throws()
    {
        var act = () => Address.Create(postalCode: new string('1', Address.PostalCodeMaxLength + 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToLines_FullAddress_FormatsLikeAnEnvelopeWithGermanCountryName()
    {
        var address = Address.Create("Bahnhofstrasse 1", null, "8001", "Zürich", "ZH", "CH");

        address.ToLines().Should().Equal("Bahnhofstrasse 1", "8001 Zürich", "ZH", "Schweiz");
    }

    [Fact]
    public void ToLines_CityWithoutPostalCode_HasNoLeadingSpace()
    {
        Address.Create(city: "Berlin").ToLines().Should().Equal("Berlin");
    }
}
