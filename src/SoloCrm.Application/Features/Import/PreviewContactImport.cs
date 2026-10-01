using FluentValidation;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Import;

/// <summary>
/// Reads an uploaded CSV file and returns its columns, the first rows and a suggested mapping (US-16 AK1, AK4).
/// Without an explicit template the HubSpot export is recognized by its columns.
/// </summary>
public static class PreviewContactImport
{
    public const int PreviewRows = 10;

    public sealed record Query(ReadOnlyMemory<byte> Content, ImportTemplate? Template = null);

    public sealed record Column(int Index, string Header);

    public sealed record Result(
        IReadOnlyList<Column> Columns,
        IReadOnlyList<CsvRow> Rows,
        int RowCount,
        char Delimiter,
        string EncodingName,
        ImportMapping SuggestedMapping);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Template).IsInEnum().WithMessage("Unbekannte Vorlage.");
        }
    }

    public sealed class Handler(IValidator<Query> validator) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(query, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            var parsed = CsvDocument.Parse(query.Content);
            if (parsed.IsFailure)
            {
                return parsed.Error;
            }

            var document = parsed.Value;
            var template = query.Template ?? ImportTemplates.Detect(document.Headers);
            return new Result(
                [.. document.Headers.Select((header, index) => new Column(index, header))],
                [.. document.Rows.Take(PreviewRows)],
                document.Rows.Count,
                document.Delimiter,
                document.EncodingName,
                ImportTemplates.Suggest(template, document.Headers));
        }
    }
}
