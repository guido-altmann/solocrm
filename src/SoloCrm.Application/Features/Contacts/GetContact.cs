using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// Loads the master data of one contact (edit dialog).
/// </summary>
public static class GetContact
{
    public sealed record Query(Guid Id);

    public sealed record Result(
        Guid Id,
        string? FirstName,
        string? LastName,
        string? Email,
        string? Phone,
        string? JobTitle,
        string? LinkedInUrl,
        Guid? OrganizationId,
        string? OrganizationName,
        OrganizationType? OrganizationType,
        LeadSource? Source,
        bool IsArchived,
        AddressData Address);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var contact = await db.Contacts
                .AsNoTracking()
                .Where(c => c.Id == query.Id)
                .Select(c => new Result(
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    c.Email,
                    c.Phone,
                    c.JobTitle,
                    c.LinkedInUrl,
                    c.OrganizationId,
                    c.Organization!.Name,
                    c.Organization!.Type,
                    c.Source,
                    c.IsArchived,
                    new AddressData(c.Address.Street, c.Address.Street2, c.Address.PostalCode, c.Address.City, c.Address.Region, c.Address.CountryCode)))
                .SingleOrDefaultAsync(cancellationToken);

            return contact is null ? ContactErrors.NotFound : contact;
        }
    }
}
