using System.Text;
using SoloCrm.Application.Features.Import;

namespace SoloCrm.Application.Tests.Features.Import;

public sealed class CsvDocumentTests
{
    [Fact]
    public void Parse_HubSpotExport_KeepsDuplicateHeadersByIndexAndUnescapesQuotes()
    {
        var document = CsvDocument.Parse(HubSpotSample.File(HubSpotSample.Row("4711", "Ada", "Lovelace"))).Value;

        document.Delimiter.Should().Be(',');
        document.Headers.Should().HaveCount(20);
        document.Headers.Count(h => h == "Billing Contact IDs").Should().Be(3);
        document.Headers[17].Should().Be("Date entered \"Kunde (Lifecycle Stage Pipeline)\"");
        var row = document.Rows.Should().ContainSingle().Subject;
        row.Number.Should().Be(2);
        row.Get(0).Should().Be("4711");
        row.Get(7).Should().Be("1;2", "a semicolon inside quotes is not a delimiter");
    }

    [Fact]
    public void Parse_SemicolonWindows1252_DetectsDelimiterAndEncoding()
    {
        var windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var content = windows1252.GetBytes("Vorname;Nachname;Firma\r\nJürgen;Müller;Bäckerei Groß & Söhne\r\n");

        var document = CsvDocument.Parse(content).Value;

        document.Delimiter.Should().Be(';');
        document.EncodingName.Should().Be("Windows-1252");
        document.Rows.Single().Values.Should().Equal("Jürgen", "Müller", "Bäckerei Groß & Söhne");
    }

    [Fact]
    public void Parse_Utf8WithBom_StripsBomFromFirstHeader()
    {
        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Email,Name\nada@example.test,Ada Lovelace\n")).ToArray();

        var document = CsvDocument.Parse(content).Value;

        document.EncodingName.Should().Be("UTF-8 (BOM)");
        document.Headers[0].Should().Be("Email");
    }

    [Fact]
    public void Parse_Utf8WithoutBom_IsRecognizedAsUtf8()
    {
        var document = CsvDocument.Parse(Encoding.UTF8.GetBytes("Name\nJosé Müller\n")).Value;

        document.EncodingName.Should().Be("UTF-8");
        document.Rows.Single().Get(0).Should().Be("José Müller");
    }

    [Fact]
    public void Parse_BlankLinesMultilineValuesAndShortRows_NumbersRowsLikeASpreadsheet()
    {
        var content = Encoding.UTF8.GetBytes("Name,Notiz,Firma\nAda,\"Zeile 1\nZeile 2\",Contoso\n\nGrace\n");

        var document = CsvDocument.Parse(content).Value;

        document.Rows.Should().HaveCount(2);
        document.Rows[0].Get(1).Should().Be("Zeile 1\nZeile 2");
        document.Rows[1].Get(0).Should().Be("Grace");
        document.Rows[1].Get(2).Should().BeNull("missing cells count as empty");
        document.Rows[1].Number.Should().BeGreaterThan(document.Rows[0].Number);
    }

    [Fact]
    public void Parse_BlankHeader_GetsColumnName()
    {
        var document = CsvDocument.Parse(Encoding.UTF8.GetBytes("Name,,Firma\nAda,x,Contoso\n")).Value;

        document.Headers.Should().Equal("Name", "Spalte 2", "Firma");
    }

    [Fact]
    public void Parse_EmptyOrHeaderlessFile_ReturnsEmptyFile()
    {
        CsvDocument.Parse(Array.Empty<byte>()).Error.Should().Be(ImportErrors.EmptyFile);
        CsvDocument.Parse(Encoding.UTF8.GetBytes("\n\n")).Error.Should().Be(ImportErrors.EmptyFile);
    }

    [Fact]
    public void Parse_TooLarge_ReturnsFileTooLarge()
    {
        CsvDocument.Parse(new byte[CsvDocument.MaxBytes + 1]).Error.Should().Be(ImportErrors.FileTooLarge);
    }

    [Fact]
    public void Parse_TooManyRows_ReturnsTooManyRows()
    {
        var content = Encoding.UTF8.GetBytes("Name\n" + string.Concat(Enumerable.Repeat("x\n", CsvDocument.MaxRows + 1)));

        CsvDocument.Parse(content).Error.Should().Be(ImportErrors.TooManyRows);
    }

    [Fact]
    public void Parse_ExactlyMaxRows_Succeeds()
    {
        var content = Encoding.UTF8.GetBytes("Name\n" + string.Concat(Enumerable.Repeat("x\n", CsvDocument.MaxRows)));

        CsvDocument.Parse(content).Value.Rows.Should().HaveCount(CsvDocument.MaxRows);
    }
}
