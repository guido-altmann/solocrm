using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SoloCrm.Application.Features.Mail;

/// <summary>
/// Turns the body of an email into plain text for the activity (ADR-013): converts HTML, drops quoted earlier
/// messages and machine-recognizable signatures. The cleanup is heuristic and loses nothing when unsure: forwarded
/// messages are kept as they are, and if nothing remains after the cut, the complete text is returned.
/// <para>
/// Not removed: free-form signatures ("Mit freundlichen Grüßen" and the lines below), because they cannot be told
/// apart from the message reliably. Recognized are the RFC delimiter <c>--</c> and the footers of mobile mail apps.
/// </para>
/// </summary>
public static partial class MailTextCleaner
{
    private const string QuoteMarker = "\u0001QUOTE\u0001";

    private static readonly string[] ForwardSubjectPrefixes = ["WG:", "FW:", "FWD:", "Weiterleitung:"];

    /// <param name="content">Body as delivered by the mail source.</param>
    /// <param name="isHtml">Whether <paramref name="content"/> is HTML.</param>
    /// <param name="subject">Subject; a forwarded message ("WG:", "Fwd:") is kept without any cut.</param>
    /// <returns>Plain text; empty if the mail has no text.</returns>
    public static string Clean(string? content, bool isHtml, string? subject = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var isForward = IsForward(subject);
        var text = Normalize(isHtml ? HtmlToText(content, markQuotes: !isForward) : content);
        var full = Normalize(text.Replace(QuoteMarker, string.Empty, StringComparison.Ordinal));
        if (isForward || HasForwardMarker(text))
        {
            return full;
        }

        var kept = Normalize(string.Join('\n', KeepOwnText(text.Split('\n'))));
        return kept.Length > 0 ? kept : full;
    }

    private static bool IsForward(string? subject) =>
        subject is not null && ForwardSubjectPrefixes.Any(p => subject.TrimStart().StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static bool HasForwardMarker(string text) => text.Split('\n').Any(l => ForwardMarkerLine().IsMatch(l));

    private static List<string> KeepOwnText(string[] lines)
    {
        var kept = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line == QuoteMarker || OriginalMessageLine().IsMatch(line) || IsHeaderBlock(lines, i) || IsSignatureStart(line))
            {
                break;
            }

            if (line.StartsWith('>'))
            {
                continue;
            }

            if (AttributionLength(lines, i) is var length and > 0)
            {
                // Inline replies: an attribution followed by "> ..." lines only introduces the quoted lines.
                if (!NextNonBlankStartsWithQuote(lines, i + length))
                {
                    break;
                }

                i += length - 1;
                continue;
            }

            kept.Add(line);
        }

        return kept;
    }

    /// <summary>Number of lines (1 to 3) of an attribution like "Am 14.10.2026 schrieb Max:", or 0.</summary>
    private static int AttributionLength(string[] lines, int index)
    {
        if (!AttributionStart().IsMatch(lines[index]))
        {
            return 0;
        }

        var joined = lines[index];
        for (var length = 1; length <= 3 && index + length <= lines.Length; length++)
        {
            if (length > 1)
            {
                joined += " " + lines[index + length - 1].Trim();
            }

            if (AttributionFull().IsMatch(joined))
            {
                return length;
            }
        }

        return 0;
    }

    private static bool NextNonBlankStartsWithQuote(string[] lines, int from)
    {
        for (var i = from; i < lines.Length; i++)
        {
            if (lines[i].Length > 0)
            {
                return lines[i].StartsWith('>');
            }
        }

        return false;
    }

    /// <summary>The header block of a quoted message: "Von:" followed shortly by "Gesendet:", "Datum:", "Sent:" or "Date:".</summary>
    private static bool IsHeaderBlock(string[] lines, int index)
    {
        if (!HeaderFrom().IsMatch(lines[index]))
        {
            return false;
        }

        for (var i = index + 1; i < lines.Length && i <= index + 5; i++)
        {
            if (HeaderFollow().IsMatch(lines[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSignatureStart(string line) => SignatureDelimiter().IsMatch(line) || MobileFooter().IsMatch(line);

    private static string HtmlToText(string html, bool markQuotes)
    {
        var text = HtmlComments().Replace(html, string.Empty);
        text = HtmlHiddenBlocks().Replace(text, string.Empty);
        if (markQuotes)
        {
            text = HtmlQuoteStart().Replace(text, m => $"\n{QuoteMarker}\n{m.Value}");
        }

        text = HtmlLink().Replace(text, LinkToText);
        text = HtmlLineBreak().Replace(text, "\n");
        text = HtmlListItem().Replace(text, "\n- ");
        text = HtmlBlockBoundary().Replace(text, "\n");
        text = HtmlCellBoundary().Replace(text, " ");
        text = HtmlTag().Replace(text, string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    private static string LinkToText(Match match)
    {
        var url = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
        var label = WebUtility.HtmlDecode(HtmlTag().Replace(match.Groups["label"].Value, string.Empty)).Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return label;
        }

        return label.Length == 0 ? url : label.Contains(url, StringComparison.OrdinalIgnoreCase) || label.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? label
            : $"{label} ({url})";
    }

    private static string Normalize(string text)
    {
        var cleaned = InvisibleCharacters().Replace(text, string.Empty).Replace(' ', ' ').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var builder = new StringBuilder(cleaned.Length);
        foreach (var line in cleaned.Split('\n'))
        {
            builder.Append(line.TrimEnd()).Append('\n');
        }

        return ManyBlankLines().Replace(builder.ToString(), "\n\n").Trim();
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComments();

    [GeneratedRegex(@"<(head|style|script)\b.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlHiddenBlocks();

    // Start of the quoted earlier message in the HTML of the common mail clients: Outlook ("appendonsend",
    // "divRplyFwdMsg", old "border-top" header), Gmail, Thunderbird, Apple Mail, Yahoo and any <blockquote>.
    [GeneratedRegex(
        """<(div\b[^>]*\bid\s*=\s*["']?(appendonsend|divRplyFwdMsg)\b|hr\b[^>]*\bid\s*=\s*["']?stopSpelling|(div|span)\b[^>]*\bclass\s*=\s*["'][^"']*\b(gmail_quote|gmail_attr|moz-cite-prefix|yahoo_quoted)\b|blockquote\b|div\b[^>]*border-top:\s*solid\s*#(E1E1E1|B5C4DF))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlQuoteStart();

    [GeneratedRegex("""<a\b[^>]*\bhref\s*=\s*["'](?<url>[^"']*)["'][^>]*>(?<label>.*?)</a\s*>""", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlLink();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlLineBreak();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlListItem();

    [GeneratedRegex(@"</?(p|div|tr|table|ul|ol|h[1-6]|blockquote|hr|pre|section|article|header|footer)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlBlockBoundary();

    [GeneratedRegex(@"</?(td|th)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlCellBoundary();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.Singleline)]
    private static partial Regex HtmlTag();

    [GeneratedRegex("[​‌‍⁠﻿]")]
    private static partial Regex InvisibleCharacters();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBlankLines();

    [GeneratedRegex(@"^(Am|On)\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AttributionStart();

    [GeneratedRegex(@"^(Am|On)\s.{4,300}?\s(schrieb|wrote)\b.{0,120}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AttributionFull();

    [GeneratedRegex(@"^[-_–—\s]{2,}(Ursprüngliche Nachricht|Original Message|Originalnachricht|Original-Nachricht)[-_–—\s]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginalMessageLine();

    [GeneratedRegex(@"^(Von|From):\s*\S", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderFrom();

    [GeneratedRegex(@"^(Gesendet|Sent|Datum|Date):\s*\S", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderFollow();

    [GeneratedRegex(@"^[-_\s]*(Forwarded message|Weitergeleitete Nachricht|Begin forwarded message|Anfang der weitergeleiteten Nachricht)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForwardMarkerLine();

    [GeneratedRegex(@"^--\s?$")]
    private static partial Regex SignatureDelimiter();

    [GeneratedRegex(
        @"^(Gesendet von meinem|Von meinem .{1,40} gesendet|Sent from my|Sent from Outlook for|Get Outlook for|Holen Sie sich Outlook für|Outlook für (iOS|Android) (abrufen|herunterladen))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MobileFooter();
}
