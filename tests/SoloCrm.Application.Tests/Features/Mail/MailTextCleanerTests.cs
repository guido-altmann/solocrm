using SoloCrm.Application.Features.Mail;

namespace SoloCrm.Application.Tests.Features.Mail;

public sealed class MailTextCleanerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n ")]
    public void Clean_NoContent_ReturnsEmpty(string? content)
    {
        MailTextCleaner.Clean(content, isHtml: false).Should().BeEmpty();
        MailTextCleaner.Clean(content, isHtml: true).Should().BeEmpty();
    }

    [Fact]
    public void Clean_PlainTextWithoutQuote_KeepsTextAndNormalizesWhitespace()
    {
        var text = "Hallo Max,\r\n\r\n\r\n\r\ndanke für das Angebot.   \r\nIch melde mich morgen.\r\n";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be("Hallo Max,\n\ndanke für das Angebot.\nIch melde mich morgen.");
    }

    [Fact]
    public void Clean_ZeroWidthCharactersAndNonBreakingSpaces_AreNormalized()
    {
        MailTextCleaner.Clean("Hallo Welt​!﻿", isHtml: false).Should().Be("Hallo Welt!");
    }

    [Fact]
    public void Clean_Html_ConvertsStructureEntitiesAndLinks()
    {
        const string html = """
            <html><head><title>Betreff</title><style>p { color: red }</style></head>
            <body><!--[if mso]><p>Konditional</p><![endif]-->
            <p>Hallo&nbsp;Max,</p>
            <p>Preis: 95&nbsp;&euro; &amp; Reisekosten<br>Start: <b>1. Dezember</b></p>
            <ul><li>Remote</li><li>Vor Ort</li></ul>
            <p>Siehe <a href="https://example.test/angebot">Angebot</a> oder <a href="https://example.test/x">https://example.test/x</a>
            oder <a href="mailto:max@example.test">max@example.test</a>.</p>
            <script>alert(1)</script></body></html>
            """;

        var text = MailTextCleaner.Clean(html, isHtml: true);

        text.Should().Be(
            "Hallo Max,\n\nPreis: 95 € & Reisekosten\nStart: 1. Dezember\n\n- Remote\n- Vor Ort\n\n" +
            "Siehe Angebot (https://example.test/angebot) oder https://example.test/x\noder max@example.test.");
    }

    [Fact]
    public void Clean_OutlookHtmlReplyGerman_DropsQuotedMessage()
    {
        const string html = """
            <html><body><div>Passt, ich nehme das Angebot an.</div>
            <div id="appendonsend"></div>
            <hr style="display:inline-block;width:98%" tabindex="-1">
            <div id="divRplyFwdMsg" dir="ltr"><font face="Calibri"><b>Von:</b> Max Mustermann<br><b>Gesendet:</b> Dienstag, 13. Oktober 2026 10:00<br><b>An:</b> Guido<br><b>Betreff:</b> Angebot</font></div>
            <div>Hallo Guido, anbei das Angebot.</div></body></html>
            """;

        MailTextCleaner.Clean(html, isHtml: true).Should().Be("Passt, ich nehme das Angebot an.");
    }

    [Fact]
    public void Clean_OutlookDesktopHtmlReply_DropsQuotedMessage()
    {
        const string html = """
            <div><p>Gerne, Termin steht.</p></div>
            <div><div style='border:none;border-top:solid #E1E1E1 1.0pt;padding:3.0pt 0cm 0cm 0cm'><p><b>From:</b> Max<br><b>Sent:</b> Tuesday</p></div></div>
            <p>Alter Text</p>
            """;

        MailTextCleaner.Clean(html, isHtml: true).Should().Be("Gerne, Termin steht.");
    }

    [Fact]
    public void Clean_GmailHtmlReply_DropsQuotedMessage()
    {
        const string html = """
            <div dir="ltr">Sounds good, let's do Monday.</div><br>
            <div class="gmail_quote"><div dir="ltr" class="gmail_attr">On Tue, Oct 13, 2026 at 10:00 AM Max &lt;max@example.test&gt; wrote:<br></div>
            <blockquote class="gmail_quote" style="margin:0px 0px 0px 0.8ex"><div>Can we meet on Monday?</div></blockquote></div>
            """;

        MailTextCleaner.Clean(html, isHtml: true).Should().Be("Sounds good, let's do Monday.");
    }

    [Fact]
    public void Clean_AppleMailHtmlReply_DropsQuotedMessage()
    {
        const string html = """
            <html><body><div>Danke, passt.</div><div><br><blockquote type="cite"><div>Am 13.10.2026 um 10:00 schrieb Max &lt;max@example.test&gt;:</div><br><div>Termin?</div></blockquote></div></body></html>
            """;

        MailTextCleaner.Clean(html, isHtml: true).Should().Be("Danke, passt.");
    }

    [Fact]
    public void Clean_ThunderbirdHtmlReply_DropsQuotedMessage()
    {
        const string html = """
            <p>Ja, gerne.</p>
            <div class="moz-cite-prefix">Am 13.10.2026 um 10:00 schrieb Max:<br></div>
            <blockquote type="cite"><p>Hast du Zeit?</p></blockquote>
            """;

        MailTextCleaner.Clean(html, isHtml: true).Should().Be("Ja, gerne.");
    }

    [Fact]
    public void Clean_PlainTextOutlookHeaderBlock_DropsQuotedMessage()
    {
        const string text = "Alles klar, bis Montag.\n\nVon: Max Mustermann <max@example.test>\nGesendet: Dienstag, 13. Oktober 2026 10:00\nAn: Guido\nBetreff: Termin\n\nPasst Montag?";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be("Alles klar, bis Montag.");
    }

    [Fact]
    public void Clean_PlainTextEnglishHeaderBlock_DropsQuotedMessage()
    {
        const string text = "Confirmed.\n\nFrom: Max <max@example.test>\nSent: Tuesday, October 13, 2026 10:00 AM\nTo: Guido\nSubject: Meeting\n\nMonday?";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be("Confirmed.");
    }

    [Theory]
    [InlineData("-----Ursprüngliche Nachricht-----")]
    [InlineData("-----Original Message-----")]
    [InlineData("----- Originalnachricht -----")]
    public void Clean_OriginalMessageSeparator_DropsEverythingBelow(string separator)
    {
        var text = $"Danke!\n\n{separator}\nVon: Max\nAlter Text";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be("Danke!");
    }

    [Fact]
    public void Clean_ListMentioningFromWithoutDate_IsKept()
    {
        const string text = "Eckdaten:\nVon: Dezember bis Mai\nBis: Mai\nRemote";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be(text);
    }

    [Fact]
    public void Clean_AttributionWithQuotedLines_DropsAttributionAndQuote()
    {
        const string text = "Ja, das passt.\n\nAm 13.10.2026 um 10:00 schrieb Max Mustermann:\n> Passt dir Montag?\n> Gruß Max";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be("Ja, das passt.");
    }

    [Fact]
    public void Clean_InlineReply_KeepsAnswersBetweenQuotedLines()
    {
        const string text = "Am 13.10.2026 schrieb Max:\n> Passt dir Montag?\n\nJa, Montag passt.\n\n> Und der Preis?\n\nDer Preis ist verhandelbar.";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be("Ja, Montag passt.\n\nDer Preis ist verhandelbar.");
    }

    [Fact]
    public void Clean_AttributionWrappedOverTwoLines_IsRecognized()
    {
        const string text = "Einverstanden.\n\nOn Tue, Oct 13, 2026 at 10:00 AM Max Mustermann\n<max@example.test> wrote:\n\nOld text";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be("Einverstanden.");
    }

    [Fact]
    public void Clean_SentenceStartingWithAmWithoutWrote_IsKept()
    {
        const string text = "Am Montag habe ich Zeit.\nAm Dienstag nicht.";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be(text);
    }

    [Theory]
    [InlineData("-- ")]
    [InlineData("--")]
    public void Clean_SignatureDelimiter_DropsSignature(string delimiter)
    {
        var text = $"Bis morgen.\n{delimiter}\nMax Mustermann\nContoso GmbH";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be("Bis morgen.");
    }

    [Theory]
    [InlineData("Gesendet von meinem iPhone")]
    [InlineData("Von meinem iPhone gesendet")]
    [InlineData("Von meinem Samsung Galaxy Smartphone gesendet")]
    [InlineData("Sent from my iPhone")]
    [InlineData("Get Outlook for iOS")]
    [InlineData("Holen Sie sich Outlook für iOS")]
    public void Clean_MobileFooter_IsDropped(string footer)
    {
        MailTextCleaner.Clean($"Komme gleich.\n\n{footer}", isHtml: false).Should().Be("Komme gleich.");
    }

    [Fact]
    public void Clean_FreeFormSignature_IsKept()
    {
        const string text = "Bis morgen.\n\nMit freundlichen Grüßen\nMax Mustermann";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be(text);
    }

    [Theory]
    [InlineData("WG: Angebot")]
    [InlineData("Fwd: Angebot")]
    [InlineData("FW: Angebot")]
    [InlineData("  Weiterleitung: Angebot")]
    public void Clean_ForwardedBySubject_KeepsEverything(string subject)
    {
        const string html = "<div>Zur Info.</div><div id=\"divRplyFwdMsg\"><b>Von:</b> Max<br><b>Gesendet:</b> Dienstag</div><div>Weitergeleiteter Inhalt</div>";

        var text = MailTextCleaner.Clean(html, isHtml: true, subject);

        text.Should().Contain("Zur Info.").And.Contain("Weitergeleiteter Inhalt");
    }

    [Fact]
    public void Clean_ForwardMarkerInBody_KeepsEverything()
    {
        const string text = "Zur Info.\n\n---------- Forwarded message ---------\nFrom: Max\nDate: Tue\n\nInhalt";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be(text);
    }

    [Fact]
    public void Clean_OnlyQuotedText_FallsBackToCompleteText()
    {
        const string html = "<blockquote><p>Nur zitierter Text</p></blockquote>";

        MailTextCleaner.Clean(html, isHtml: true).Should().Be("Nur zitierter Text");
    }

    [Fact]
    public void Clean_ReplyBelowQuote_FallsBackToCompleteText()
    {
        const string text = "-----Original Message-----\nVon: Max\nAlter Text\n\nMeine Antwort darunter";

        MailTextCleaner.Clean(text, isHtml: false).Should().Be(text);
    }

    [Fact]
    public void Clean_OnlySignature_FallsBackToCompleteText()
    {
        MailTextCleaner.Clean("-- \nMax Mustermann", isHtml: false).Should().Be("--\nMax Mustermann");
    }

    [Fact]
    public void Clean_LargeMalformedHtml_FinishesQuickly()
    {
        var html = string.Concat(Enumerable.Repeat("<a href=\"https://example.test\">x <div id=\"a\" <p", 20_000));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        MailTextCleaner.Clean(html, isHtml: true);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
