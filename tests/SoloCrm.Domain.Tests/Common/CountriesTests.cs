using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Tests.Common;

public sealed class CountriesTests
{
    [Fact]
    public void All_Always_HasUniqueTwoLetterCodesAndGermanNames()
    {
        Countries.All.Should().HaveCount(249);
        Countries.All.Select(c => c.Code).Should().OnlyHaveUniqueItems().And.AllSatisfy(c => c.Should().MatchRegex("^[A-Z]{2}$"));
        Countries.All.Should().AllSatisfy(c => c.GermanName.Should().NotBeNullOrWhiteSpace());
    }

    [Theory]
    [InlineData("DE", "DE")]
    [InlineData("de", "DE")]
    [InlineData("Deutschland", "DE")]
    [InlineData("Germany", "DE")]
    [InlineData(" germany ", "DE")]
    [InlineData("Schweiz", "CH")]
    [InlineData("Switzerland", "CH")]
    [InlineData("Österreich", "AT")]
    [InlineData("USA", "US")]
    [InlineData("United Kingdom", "GB")]
    [InlineData("UK", "GB")]
    [InlineData("Netherlands", "NL")]
    public void Find_CodeNameOrAlias_ReturnsCountry(string value, string code)
    {
        Countries.Find(value)!.Code.Should().Be(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("Atlantis")]
    public void Find_Unknown_ReturnsNull(string? value)
    {
        Countries.Find(value).Should().BeNull();
    }

    [Fact]
    public void All_Always_IsOrderedByGermanName()
    {
        Countries.All.Take(3).Select(c => c.GermanName).Should().Equal("Afghanistan", "Ägypten", "Ålandinseln");
        Countries.ByIsoCode("DE")!.GermanName.Should().Be("Deutschland");
    }
}
