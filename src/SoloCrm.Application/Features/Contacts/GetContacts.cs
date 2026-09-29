using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// Pages through contacts, optionally filtered by a case-insensitive substring of name or email.
/// </summary>
public static class GetContacts
{
    public const int DefaultPageSize = Paging.DefaultPageSize;

    public sealed record Query(string? Search = null, int PageIndex = 0, int PageSize = DefaultPageSize) : IPagedQuery;

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
            Include(new PagingValidator<Query>());
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Query> validator)
        : IQueryHandler<Query, Result>
    {
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
                var pattern = LikePattern.Contains(query.Search);
                contacts = contacts.Where(c =>
                    EF.Functions.ILike(c.FirstName ?? "", pattern, LikePattern.Escape)
                    || EF.Functions.ILike(c.LastName ?? "", pattern, LikePattern.Escape)
                    || EF.Functions.ILike((c.FirstName ?? "") + " " + (c.LastName ?? ""), pattern, LikePattern.Escape)
                    || EF.Functions.ILike(c.Email ?? "", pattern, LikePattern.Escape));
            }

            var totalCount = await contacts.CountAsync(cancellationToken);

            var items = await contacts
                .OrderBy(c => c.LastName ?? c.FirstName)
                .ThenBy(c => c.FirstName)
                .ThenBy(c => c.Id)
                .Page(query)
                .Select(c => new Item(c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.CreatedAt))
                .ToListAsync(cancellationToken);

            return new Result(items, totalCount);
        }
    }
}
