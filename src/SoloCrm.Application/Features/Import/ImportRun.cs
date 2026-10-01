using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Import;

public sealed record ImportProgress(int Processed, int Total);

/// <param name="Row">Row number in the file (header = 1).</param>
public sealed record ImportRowIssue(int Row, string Reason);

/// <summary>The report of an import (US-16 AK3): counts plus skipped and failed rows with their reason.</summary>
public sealed record ImportReport(
    int Created,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<ImportRowIssue> Skips,
    IReadOnlyList<ImportRowIssue> Errors);

/// <summary>Outcome of one row.</summary>
internal abstract record RowResult
{
    public sealed record Created : RowResult;

    public sealed record Updated : RowResult;

    public sealed record Skipped(string Reason) : RowResult;

    public sealed record Failed(string Reason) : RowResult;
}

/// <summary>
/// The rows of one block share a DbContext and a transaction. Lookup entries the block adds (new records, rows seen)
/// become visible to later blocks only in <see cref="Commit"/>, after the block was saved.
/// </summary>
internal interface IImportBlock
{
    Task<RowResult> ImportAsync(CsvRow row, CancellationToken cancellationToken);

    void Commit();
}

/// <summary>
/// The procedure shared by the contact and the organization import (iteration 5 decision 10): parse, check the mapping,
/// then process blocks of <see cref="BlockSize"/> rows with one transaction each and report progress after each block.
/// </summary>
internal static class ImportRun
{
    public const int BlockSize = 100;

    /// <summary>Parses the file and checks that every mapped column exists.</summary>
    public static Result<CsvDocument> Prepare(ReadOnlyMemory<byte> content, ImportMapping mapping)
    {
        var parsed = CsvDocument.Parse(content);
        if (parsed.IsFailure)
        {
            return parsed;
        }

        var columnCount = parsed.Value.Headers.Count;
        return mapping.Fields.Any(f => f.Column >= columnCount || f.FallbackColumn >= columnCount)
            ? new ValidationError(new Dictionary<string, string[]>
            {
                ["Mapping"] = ["Eine zugeordnete Spalte existiert in der Datei nicht."],
            })
            : parsed;
    }

    public static async Task<ImportReport> RunAsync(
        CsvDocument document,
        ICrmDbContextFactory dbFactory,
        Func<ICrmDbContext, IImportBlock> createBlock,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var created = 0;
        var updated = 0;
        var skips = new List<ImportRowIssue>();
        var errors = new List<ImportRowIssue>();
        var processed = 0;

        foreach (var rows in document.Rows.Chunk(BlockSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var block = createBlock(db);
            var blockCreated = new List<int>();
            var blockUpdated = new List<int>();
            var blockSkips = new List<ImportRowIssue>();
            foreach (var row in rows)
            {
                switch (await block.ImportAsync(row, cancellationToken))
                {
                    case RowResult.Created:
                        blockCreated.Add(row.Number);
                        break;
                    case RowResult.Updated:
                        blockUpdated.Add(row.Number);
                        break;
                    case RowResult.Skipped skipped:
                        blockSkips.Add(new ImportRowIssue(row.Number, skipped.Reason));
                        break;
                    case RowResult.Failed failed:
                        errors.Add(new ImportRowIssue(row.Number, failed.Reason));
                        break;
                }
            }

            skips.AddRange(blockSkips);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                block.Commit();
                created += blockCreated.Count;
                updated += blockUpdated.Count;
            }
            catch (DbUpdateException)
            {
                // E.g. a record with the same unique value was created in the UI meanwhile; the unique index protects the data.
                var reason = $"Speichern fehlgeschlagen; die Zeilen {rows[0].Number}–{rows[^1].Number} wurden nicht übernommen.";
                errors.AddRange(blockCreated.Concat(blockUpdated).Select(number => new ImportRowIssue(number, reason)));
            }

            processed += rows.Length;
            progress?.Report(new ImportProgress(processed, document.Rows.Count));
        }

        return new ImportReport(created, updated, skips.Count, errors.Count, [.. skips.OrderBy(i => i.Row)], [.. errors.OrderBy(i => i.Row)]);
    }

    /// <summary>The color for the next new tag; new tags get the palette colors in turn (iteration 4 decision 7).</summary>
    public static Task<string?> LastTagColorAsync(ICrmDbContext db, CancellationToken cancellationToken) =>
        db.Tags
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Select(t => t.Color)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>An existing tag with this name (case-insensitive, citext) or a new one with the next palette color.</summary>
    public static async Task<Tag> ResolveTagAsync(
        ICrmDbContext db, Dictionary<string, Tag> cache, string name, Func<string?> lastColor, Action<string> useColor, CancellationToken cancellationToken)
    {
        var normalized = Tag.NormalizeName(name);
        if (cache.TryGetValue(normalized, out var tag))
        {
            return tag;
        }

        tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == normalized, cancellationToken);
        if (tag is null)
        {
            var color = TagPalette.Next(lastColor());
            tag = Tag.Create(normalized, color);
            db.Tags.Add(tag);
            useColor(color);
        }

        cache[normalized] = tag;
        return tag;
    }
}

/// <summary>Rules shared by the import commands.</summary>
internal static class ImportValidation
{
    public static void AddMappingRules<T>(this AbstractValidator<T> validator, Func<T, ImportMapping?> mapping, ImportTarget target, string requiredMessage, params ImportField[] required)
    {
        validator.RuleFor(c => mapping(c))
            .NotNull()
            .WithMessage("Bitte die Spalten zuordnen.")
            .OverridePropertyName("Mapping");

        validator.RuleFor(c => mapping(c)!)
            .Must(m => Enum.IsDefined(m.Template))
            .WithMessage("Unbekannte Vorlage.")
            .Must(m => m.Target == target)
            .WithMessage("Die Zuordnung passt nicht zum Zieltyp.")
            .Must(m => m.Fields.Any(f => f.Column is not null && required.Contains(f.Field)))
            .WithMessage(requiredMessage)
            .Must(m => m.Fields.GroupBy(f => f.Field).All(g => g.Count() == 1))
            .WithMessage("Jedes Zielfeld darf nur einmal zugeordnet werden.")
            .Must(m => m.Fields.All(f => f.Column is null || ImportFields.For(target).Contains(f.Field)))
            .WithMessage("Ein Zielfeld passt nicht zum Zieltyp.")
            .Must(m => m.Fields.All(f => f.Column is null or >= 0 && f.FallbackColumn is null or >= 0))
            .WithMessage("Ungültige Spalte.")
            .OverridePropertyName("Mapping")
            .When(c => mapping(c) is not null);
    }
}
