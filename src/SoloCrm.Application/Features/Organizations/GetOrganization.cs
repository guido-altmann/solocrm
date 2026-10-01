using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>
/// Loads the master data of one organization (edit dialog).
/// </summary>
public static class GetOrganization
{
    public sealed record Query(Guid Id);

    public sealed record Result(
        Guid Id,
        string Name,
        OrganizationType Type,
        string? Website,
        AddressData Address,
        string? Notes,
        bool IsArchived);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var organization = await db.Organizations
                .AsNoTracking()
                .Where(o => o.Id == query.Id)
                .Select(o => new Result(o.Id, o.Name, o.Type, o.Website, new AddressData(o.Address.Street, o.Address.Street2, o.Address.PostalCode, o.Address.City, o.Address.Region, o.Address.CountryCode), o.Notes, o.IsArchived))
                .SingleOrDefaultAsync(cancellationToken);

            return organization is null ? OrganizationErrors.NotFound : organization;
        }
    }
}
