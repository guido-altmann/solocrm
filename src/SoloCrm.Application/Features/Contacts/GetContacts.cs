using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// Pages through contacts with search (name, email, organization), filters and sorting;
/// archived contacts only on request (US-04, US-05).
/// </summary>
public static class GetContacts
{
    public const int DefaultPageSize = Paging.DefaultPageSize;

    public enum SortField
    {
        Name,
        Organization,
        Email,
        CreatedAt,
    }

    public sealed record Query(
        string? Search = null,
        Guid? OrganizationId = null,
        LeadSource? Source = null,
        bool IncludeArchived = false,
        SortField SortBy = SortField.Name,
        bool SortDescending = false,
        int PageIndex = 0,
        int PageSize = DefaultPageSize) : IPagedQuery;

    public sealed record Item(
        Guid Id,
        string? FirstName,
        string? LastName,
        string? Email,
        string? Phone,
        string? JobTitle,
        Guid? OrganizationId,
        string? OrganizationName,
        LeadSource? Source,
        bool IsArchived,
        DateTimeOffset CreatedAt);

    public sealed record Result(IReadOnlyList<Item> Items, int TotalCount);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            Include(new PagingValidator<Query>());
            RuleFor(q => q.Source).IsInEnum().WithMessage("Unbekannte Quelle.");
            RuleFor(q => q.SortBy).IsInEnum().WithMessage("Unbekannte Sortierung.");
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

            if (!query.IncludeArchived)
            {
                contacts = contacts.Where(c => !c.IsArchived);
            }

            if (query.OrganizationId is { } organizationId)
            {
                contacts = contacts.Where(c => c.OrganizationId == organizationId);
            }

            if (query.Source is { } source)
            {
                contacts = contacts.Where(c => c.Source == source);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var pattern = LikePattern.Contains(query.Search);
                contacts = contacts.Where(c =>
                    EF.Functions.ILike(c.FirstName ?? "", pattern, LikePattern.Escape)
                    || EF.Functions.ILike(c.LastName ?? "", pattern, LikePattern.Escape)
                    || EF.Functions.ILike((c.FirstName ?? "") + " " + (c.LastName ?? ""), pattern, LikePattern.Escape)
                    || EF.Functions.ILike(c.Email ?? "", pattern, LikePattern.Escape)
                    || EF.Functions.ILike(c.Organization!.Name ?? "", pattern, LikePattern.Escape));
            }

            var totalCount = await contacts.CountAsync(cancellationToken);

            var items = await Sort(contacts, query.SortBy, query.SortDescending)
                .ThenBy(c => c.Id)
                .Page(query)
                .Select(c => new Item(
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    c.Email,
                    c.Phone,
                    c.JobTitle,
                    c.OrganizationId,
                    c.Organization!.Name,
                    c.Source,
                    c.IsArchived,
                    c.CreatedAt))
                .ToListAsync(cancellationToken);

            return new Result(items, totalCount);
        }

        private static IOrderedQueryable<Contact> Sort(IQueryable<Contact> source, SortField field, bool descending) =>
            field switch
            {
                SortField.Organization => descending
                    ? source.OrderByDescending(c => c.Organization!.Name)
                    : source.OrderBy(c => c.Organization!.Name),
                SortField.Email => descending ? source.OrderByDescending(c => c.Email) : source.OrderBy(c => c.Email),
                SortField.CreatedAt => descending ? source.OrderByDescending(c => c.CreatedAt) : source.OrderBy(c => c.CreatedAt),
                _ => descending
                    ? source.OrderByDescending(c => c.LastName ?? c.FirstName).ThenByDescending(c => c.FirstName)
                    : source.OrderBy(c => c.LastName ?? c.FirstName).ThenBy(c => c.FirstName),
            };
    }
}
