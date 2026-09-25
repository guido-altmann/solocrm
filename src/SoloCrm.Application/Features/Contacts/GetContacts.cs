using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// Pages through contacts, optionally filtered by a case-insensitive substring of name or email.
/// </summary>
public static class GetContacts
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <param name="PageIndex">Zero-based page index.</param>
    public sealed record Query(string? Search = null, int PageIndex = 0, int PageSize = DefaultPageSize);

    public sealed record Item(
        Guid Id,
        string? FirstName,
        string? LastName,
        string? Email,
        string? Phone,
        string? JobTitle,
        DateTimeOffset CreatedAt);

    public sealed record Result(IReadOnlyList<Item> Items, int TotalCount);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.PageIndex)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Die Seitenzahl darf nicht negativ sein.");

            RuleFor(q => q.PageSize)
                .InclusiveBetween(1, MaxPageSize)
                .WithMessage($"Die Seitengröße muss zwischen 1 und {MaxPageSize} liegen.");
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Query> validator)
        : IQueryHandler<Query, Result>
    {
        private const string LikeEscape = "\\";

        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(query, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var contacts = db.Contacts.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var pattern = $"%{EscapeLikePattern(query.Search.Trim())}%";
                contacts = contacts.Where(c =>
                    EF.Functions.ILike(c.FirstName ?? "", pattern, LikeEscape)
                    || EF.Functions.ILike(c.LastName ?? "", pattern, LikeEscape)
                    || EF.Functions.ILike((c.FirstName ?? "") + " " + (c.LastName ?? ""), pattern, LikeEscape)
                    || EF.Functions.ILike(c.Email ?? "", pattern, LikeEscape));
            }

            var totalCount = await contacts.CountAsync(cancellationToken);

            var items = await contacts
                .OrderBy(c => c.LastName ?? c.FirstName)
                .ThenBy(c => c.FirstName)
                .ThenBy(c => c.Id)
                .Skip(query.PageIndex * query.PageSize)
                .Take(query.PageSize)
                .Select(c => new Item(c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.CreatedAt))
                .ToListAsync(cancellationToken);

            return new Result(items, totalCount);
        }

        private static string EscapeLikePattern(string value) => value
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
    }
}
