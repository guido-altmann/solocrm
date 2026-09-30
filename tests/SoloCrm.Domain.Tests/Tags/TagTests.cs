using System.Globalization;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Domain.Tests.Tags;

public sealed class TagTests
{
    [Fact]
    public void Create_NameWithExtraWhitespace_IsNormalized()
    {
        var tag = Tag.Create("  Kunde \t  A ", TagPalette.Colors[0]);

        tag.Name.Should().Be("Kunde A");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_Throws(string name)
    {
        var create = () => Tag.Create(name, TagPalette.Colors[0]);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NameLongerThanMax_Throws()
    {
        var create = () => Tag.Create(new string('x', Tag.NameMaxLength + 1), TagPalette.Colors[0]);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_LowerCasePaletteColor_StoresCanonicalSpelling()
    {
        var tag = Tag.Create("Kunde", TagPalette.Colors[3].ToLowerInvariant());

        tag.Color.Should().Be(TagPalette.Colors[3]);
    }

    [Fact]
    public void ChangeColor_ColorOutsidePalette_Throws()
    {
        var tag = Tag.Create("Kunde", TagPalette.Colors[0]);

        var change = () => tag.ChangeColor("#FFFF00");

        change.Should().Throw<ArgumentException>();
        tag.Color.Should().Be(TagPalette.Colors[0]);
    }

    [Fact]
    public void Rename_NewName_IsNormalized()
    {
        var tag = Tag.Create("Kunde", TagPalette.Colors[0]);

        tag.Rename(" Stammkunde ");

        tag.Name.Should().Be("Stammkunde");
    }

    [Fact]
    public void Next_NoPreviousTag_ReturnsFirstColor() =>
        TagPalette.Next(null).Should().Be(TagPalette.Colors[0]);

    [Fact]
    public void Next_PreviousColor_ReturnsFollowingColorAndWrapsAround()
    {
        TagPalette.Next(TagPalette.Colors[0]).Should().Be(TagPalette.Colors[1]);
        TagPalette.Next(TagPalette.Colors[^1]).Should().Be(TagPalette.Colors[0]);
        TagPalette.Next("#123456").Should().Be(TagPalette.Colors[0], "unknown colors restart the rotation");
    }

    [Fact]
    public void Colors_AreDistinct() =>
        TagPalette.Colors.Should().OnlyHaveUniqueItems(c => c.ToUpperInvariant());

    /// <summary>Chips show white text on the tag color; WCAG AA requires 4.5:1 for normal text.</summary>
    [Fact]
    public void Colors_WhiteText_HasContrastOfAtLeast4Point5()
    {
        foreach (var color in TagPalette.Colors)
        {
            ContrastWithWhite(color).Should().BeGreaterThanOrEqualTo(4.5, color);
        }
    }

    private static double ContrastWithWhite(string hex)
    {
        static double Channel(string hex, int offset)
        {
            var value = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Channel(hex, 1)) + (0.7152 * Channel(hex, 3)) + (0.0722 * Channel(hex, 5));
        return 1.05 / (luminance + 0.05);
    }
}
