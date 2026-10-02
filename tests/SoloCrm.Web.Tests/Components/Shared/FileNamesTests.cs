using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class FileNamesTests
{
    [Theory]
    [InlineData("Ada Lovelace", "ada-lovelace")]
    [InlineData("Jürgen Groß-Möller", "juergen-gross-moeller")]
    [InlineData("  José  Núñez ", "jose-nunez")]
    [InlineData("O'Brien / 李", "o-brien")]
    [InlineData("李", "ohne-namen")]
    public void Slug_Name_ReturnsAsciiWithHyphens(string name, string expected) =>
        FileNames.Slug(name).Should().Be(expected);

    [Fact]
    public void ContactExport_NameAndDate_ReturnsJsonFileName() =>
        FileNames.ContactExport("Ada Lovelace", new DateOnly(2026, 10, 2)).Should().Be("kontakt-ada-lovelace-2026-10-02.json");
}
