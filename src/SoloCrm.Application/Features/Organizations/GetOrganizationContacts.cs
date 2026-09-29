using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>The contacts of an organization for its detail view; archived ones are included and flagged.</summary>
public static class GetOrganizationContacts
{
    public sealed record Query(Guid OrganizationId);

    public sealed record Item(Guid Id, string Name, string? JobTitle, string? Email, bool IsArchived);

    public sealed record Result(IReadOnlyList<Item> Items);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var items = await db.Contacts
                .AsNoTracking()
                .Where(c => c.OrganizationId == query.OrganizationId)
                .OrderBy(c => c.IsArchived)
                .ThenBy(c => c.LastName ?? c.FirstName)
                .ThenBy(c => c.FirstName)
                .Select(c => new Item(c.Id, Names.Person(c.FirstName, c.LastName), c.JobTitle, c.Email, c.IsArchived))
                .ToListAsync(cancellationToken);

            return new Result(items);
        }
    }
}
