using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// Renders activity bodies (iteration 3, decision 1). Raw HTML from the input is escaped, links and images
/// only keep http(s) and mailto URLs, and links open in a new tab so the Blazor circuit stays alive.
/// </summary>
public static class Markdown
{
    private static readonly string[] SafeSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto];

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseTaskLists()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static MarkupString ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return default;
        }

        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafe(link.Url))
            {
                link.Url = "";
            }

            if (!link.IsImage)
            {
                var attributes = link.GetAttributes();
                attributes.AddPropertyIfNotExist("target", "_blank");
                attributes.AddPropertyIfNotExist("rel", "noopener noreferrer");
            }
        }

        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (!IsSafe(link.IsEmail ? $"mailto:{link.Url}" : link.Url))
            {
                link.Url = "";
            }
        }

        return new MarkupString(Markdig.Markdown.ToHtml(document, Pipeline));
    }

    private static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // Relative URLs (no scheme) cannot execute script.
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return !url.Contains(':', StringComparison.Ordinal);
        }

        return SafeSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
    }
}
