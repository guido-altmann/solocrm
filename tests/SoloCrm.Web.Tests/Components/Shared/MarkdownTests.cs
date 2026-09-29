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
    public void ToHtml_Blank_ReturnsEmpty()
    {
        Markdown.ToHtml("  ").Value.Should().BeNullOrEmpty();
    }
}
