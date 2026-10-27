using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class MarkdownTests
{
    [Fact]
    public void ToHtml_Markdown_RendersFormatting()
    {
        var html = Markdown.ToHtml("**Budget** geklärt\n- Punkt").Value;

        html.Should().Contain("<strong>Budget</strong>").And.Contain("<li>Punkt</li>");
    }

    [Fact]
    public void ToHtml_RawHtml_IsEscaped()
    {
        var html = Markdown.ToHtml("<script>alert(1)</script> <b>fett</b>").Value;

        html.Should().NotContain("<script>").And.NotContain("<b>").And.Contain("&lt;script&gt;");
    }

    [Theory]
    [InlineData("[klick](javascript:alert(1))")]
    [InlineData("[klick](JavaScript:alert(1))")]
    [InlineData("[klick](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("<javascript:alert(1)>")]
    public void ToHtml_UnsafeLink_DropsUrl(string markdown)
    {
        var html = Markdown.ToHtml(markdown).Value;

        html.Should().NotContainEquivalentOf("javascript:").And.NotContain("data:");
    }

    [Fact]
    public void ToHtml_HttpLink_OpensInNewTab()
    {
        var html = Markdown.ToHtml("[Profil](https://example.com) und https://contoso.de").Value;

        html.Should().Contain("href=\"https://example.com\"")
            .And.Contain("target=\"_blank\"")
            .And.Contain("rel=\"noopener noreferrer\"")
            .And.Contain("href=\"https://contoso.de\"");
    }

    [Fact]
    public void ToHtml_Image_RendersLinkInsteadOfImage()
    {
        var html = Markdown.ToHtml("![Logo](https://tracker.example/p.gif)").Value;

        html.Should().NotContain("<img")
            .And.Contain("<a href=\"https://tracker.example/p.gif\"")
            .And.Contain(">Logo</a>")
            .And.Contain("rel=\"noopener noreferrer\"");
    }

    [Fact]
    public void ToHtml_ImageWithoutAltText_ShowsUrlAsLabel()
    {
        var html = Markdown.ToHtml("![](https://tracker.example/p.gif)").Value;

        html.Should().NotContain("<img").And.Contain(">https://tracker.example/p.gif</a>");
    }

    [Fact]
    public void ToHtml_UnsafeImage_DropsUrl()
    {
        var html = Markdown.ToHtml("![x](javascript:alert(1))").Value;

        html.Should().NotContain("<img").And.NotContainEquivalentOf("javascript:");
    }

    [Fact]
    public void ToHtml_LabelLooksLikeOtherUrl_ShowsTarget()
    {
        var html = Markdown.ToHtml("[https://bank.example](https://evil.example/login)").Value;

        html.Should().Contain(">https://bank.example</a> (https://evil.example/login)");
    }

    [Theory]
    [InlineData("[https://example.com](https://example.com)")]
    [InlineData("[www.example.com](https://www.example.com/)")]
    [InlineData("[Profil](https://example.com)")]
    public void ToHtml_LabelMatchesTarget_ShowsNoTarget(string markdown)
    {
        var html = Markdown.ToHtml(markdown).Value;

        html.Should().NotContain("</a> (");
    }

    [Fact]
    public void ToHtml_Blank_ReturnsEmpty()
    {
        Markdown.ToHtml("  ").Value.Should().BeNullOrEmpty();
    }
}
