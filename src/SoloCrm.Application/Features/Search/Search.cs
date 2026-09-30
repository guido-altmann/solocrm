using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Search;

/// <summary>
/// Global search of the command palette over contacts (name, e-mail, organization), organizations and requests
/// (title), typo tolerant and ordered by relevance; archived records are excluded (US-13, US-05 AK1, ADR-007).
/// Each type is queried separately with its own limit, so one type cannot crowd out the others.
/// </summary>
public static class Search
{
    /// <summary>Hits appear from two characters on (US-13 AK1).</summary>
    public const int MinLength = 2;

    public const int DefaultLimit = 5;
    public const int MaxLimit = 20;

    public sealed record Query(string? Text, int Limit = DefaultLimit);

    public sealed record ContactHit(Guid Id, string Name, string? Email, string? OrganizationName);

    public sealed record OrganizationHit(Guid Id, string Name, OrganizationType Type);

    public sealed record OpportunityHit(Guid Id, string Title, string StageName, StageStatus Status, string? ClientName);

    public sealed record Result(
        IReadOnlyList<ContactHit> Contacts,
        IReadOnlyList<OrganizationHit> Organizations,
        IReadOnlyList<OpportunityHit> Opportunities)
    {
        public static Result Empty { get; } = new([], [], []);

        public bool IsEmpty => Contacts.Count == 0 && Organizations.Count == 0 && Opportunities.Count == 0;
    }

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Limit)
                .InclusiveBetween(1, MaxLimit)
                .WithMessage($"Es können höchstens {MaxLimit} Treffer je Typ abgefragt werden.");
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

            if (SearchTerm.Parse(query.Text) is not { } term || term.Text.Length < MinLength)
            {
                return Result.Empty;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var organizationIds = await db.MatchingOrganizationIdsAsync(term, cancellationToken);
            var contacts = await db.Contacts.AsNoTracking()
                .Where(c => !c.IsArchived)
                .Where(SearchPredicates.ContactMatches(term, organizationIds))
                .OrderByDescending(SearchPredicates.ContactRelevance(term))
                .ThenBy(c => c.Id)
                .Take(query.Limit)
                .Select(c => new { c.Id, c.FirstName, c.LastName, c.Email, OrganizationName = c.Organization!.Name })
                .ToListAsync(cancellationToken);

            var organizations = await db.Organizations.AsNoTracking()
                .Where(o => !o.IsArchived)
                .Where(SearchPredicates.OrganizationMatches(term))
                .OrderByDescending(SearchPredicates.OrganizationRelevance(term))
                .ThenBy(o => o.Id)
                .Take(query.Limit)
                .Select(o => new OrganizationHit(o.Id, o.Name, o.Type))
                .ToListAsync(cancellationToken);

            var opportunities = await db.Opportunities.AsNoTracking()
                .Where(o => !o.IsArchived)
                .Where(SearchPredicates.OpportunityMatches(term))
                .OrderByDescending(SearchPredicates.OpportunityRelevance(term))
                .ThenBy(o => o.Id)
                .Take(query.Limit)
                .Select(o => new OpportunityHit(o.Id, o.Title, o.Stage!.Name, o.Stage.Status, o.ClientOrganization!.Name))
                .ToListAsync(cancellationToken);

            return new Result(
                [.. contacts.Select(c => new ContactHit(c.Id, Names.Person(c.FirstName, c.LastName), c.Email, c.OrganizationName))],
                organizations,
                opportunities);
        }
    }
}
