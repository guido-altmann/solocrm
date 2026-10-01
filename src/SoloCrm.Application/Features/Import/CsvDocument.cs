using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Import;

/// <summary>
/// A parsed CSV file (US-16). Columns are addressed by index because header names need not be unique (the HubSpot
/// export repeats <c>Billing Contact IDs</c>). Detects the encoding (UTF-8 with or without BOM, otherwise
/// Windows-1252) and the delimiter (<c>,</c>, <c>;</c> or tab) and enforces the size limits.
/// </summary>
public sealed class CsvDocument
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxRows = 10_000;

    private static readonly char[] Delimiters = [',', ';', '\t'];

    private CsvDocument(IReadOnlyList<string> headers, IReadOnlyList<CsvRow> rows, char delimiter, string encodingName)
    {
        Headers = headers;
        Rows = rows;
        Delimiter = delimiter;
        EncodingName = encodingName;
    }

    /// <summary>Header per column index; blank headers become „Spalte n“.</summary>
    public IReadOnlyList<string> Headers { get; }

    /// <summary>Data rows without the header.</summary>
    public IReadOnlyList<CsvRow> Rows { get; }

    public char Delimiter { get; }

    public string EncodingName { get; }

    public static Result<CsvDocument> Parse(ReadOnlyMemory<byte> content)
    {
        if (content.Length == 0)
        {
            return ImportErrors.EmptyFile;
        }

        if (content.Length > MaxBytes)
        {
            return ImportErrors.FileTooLarge;
        }

        var (text, encodingName) = Decode(content.Span);
        var delimiter = DetectDelimiter(text);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            IgnoreBlankLines = true,
            BadDataFound = null,
            MissingFieldFound = null,
            DetectColumnCountChanges = false,
        };

        using var reader = new StringReader(text);
        using var parser = new CsvParser(reader, configuration);

        string[]? header = null;
        var rows = new List<CsvRow>();
        while (parser.Read())
        {
            var record = parser.Record ?? [];
            if (header is null)
            {
                header = record;
                continue;
            }

            if (record.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (rows.Count == MaxRows)
            {
                return ImportErrors.TooManyRows;
            }

            rows.Add(new CsvRow(parser.Row, record));
        }

        if (header is null || header.All(string.IsNullOrWhiteSpace))
        {
            return ImportErrors.EmptyFile;
        }

        var headers = header
            .Select((name, index) => string.IsNullOrWhiteSpace(name) ? $"Spalte {index + 1}" : name.Trim())
            .ToList();
        return new CsvDocument(headers, rows, delimiter, encodingName);
    }

    /// <summary>UTF-8 (BOM or valid without), UTF-16 with BOM, otherwise Windows-1252 (Excel on Windows).</summary>
    private static (string Text, string EncodingName) Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            return (Encoding.UTF8.GetString(bytes[Encoding.UTF8.Preamble.Length..]), "UTF-8 (BOM)");
        }

        if (bytes.StartsWith(Encoding.Unicode.Preamble))
        {
            return (Encoding.Unicode.GetString(bytes[Encoding.Unicode.Preamble.Length..]), "UTF-16");
        }

        try
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            var windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
            return (windows1252.GetString(bytes), "Windows-1252");
        }
    }

    /// <summary>The candidate that occurs most often in the first line, outside of quotes; defaults to comma.</summary>
    private static char DetectDelimiter(string text)
    {
        var counts = new int[Delimiters.Length];
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is '\r' or '\n')
            {
                break;
            }
            else if (!quoted && Array.IndexOf(Delimiters, c) is var index and >= 0)
            {
                counts[index]++;
            }
        }

        var best = Array.IndexOf(counts, counts.Max());
        return counts[best] > 0 ? Delimiters[best] : ',';
    }
}

/// <param name="Number">Row number in the file as a spreadsheet shows it (header = 1).</param>
public sealed record CsvRow(int Number, IReadOnlyList<string> Values)
{
    /// <summary>The trimmed value of a column, or <c>null</c> when it is missing or blank.</summary>
    public string? Get(int? column) =>
        column is { } index && index < Values.Count && !string.IsNullOrWhiteSpace(Values[index]) ? Values[index].Trim() : null;
}

public static class ImportErrors
{
    public static Error EmptyFile { get; } =
        Error.Failure("Import.EmptyFile", "Die Datei ist leer oder hat keine Kopfzeile.");

    public static Error FileTooLarge { get; } =
        Error.Failure("Import.FileTooLarge", $"Die Datei ist größer als {CsvDocument.MaxBytes / 1024 / 1024} MB.");

    public static Error TooManyRows { get; } =
        Error.Failure("Import.TooManyRows", $"Die Datei hat mehr als {CsvDocument.MaxRows.ToString("N0", CultureInfo.GetCultureInfo("de-DE"))} Zeilen. Bitte aufteilen.");
}
