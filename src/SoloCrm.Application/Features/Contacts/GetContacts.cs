using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Search;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// Pages through contacts with search (name, email, organization; same search as the command palette, ADR-007),
/// filters (organization, source, tags OR-combined) and sorting; archived contacts only on request (US-04, US-05). Without an explicit sort field, search
/// results are ordered by relevance and everything else by name.
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
        SortField? SortBy = null,
        bool SortDescending = false,
        int PageIndex = 0,
        int PageSize = DefaultPageSize,
        IReadOnlyCollection<Guid>? TagIds = null) : IPagedQuery;

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
        DateTimeOffset CreatedAt,
        IReadOnlyList<TagRef> Tags);

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

            // Several tags are OR-combined (US-04 AK2).
            if (query.TagIds is { Count: > 0 } tagIds)
            {
                contacts = contacts.Where(c => db.ContactTags.Any(t => t.ContactId == c.Id && tagIds.Contains(t.TagId)));
            }

            var term = SearchTerm.Parse(query.Search);
            if (term is not null)
            {
                var organizationIds = await db.MatchingOrganizationIdsAsync(term, cancellationToken);
                contacts = contacts.Where(SearchPredicates.ContactMatches(term, organizationIds));
            }

            var totalCount = await contacts.CountAsync(cancellationToken);

            var sorted = query.SortBy is null && term is not null
                ? contacts.OrderByDescending(SearchPredicates.ContactRelevance(term))
                : Sort(contacts, query.SortBy ?? SortField.Name, query.SortDescending);

            var items = await sorted
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
                    c.CreatedAt,
                    db.ContactTags
                        .Where(t => t.ContactId == c.Id)
                        .OrderBy(t => t.Tag!.Name)
                        .Select(t => new TagRef(t.TagId, t.Tag!.Name, t.Tag.Color))
                        .ToList()))
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
