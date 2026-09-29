using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>
/// Archives an organization; it disappears from lists and search (US-05 AK1).
/// </summary>
public static class ArchiveOrganization
{
    public sealed record Command(Guid Id);

    public sealed record Result(Guid Id);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
            if (organization is null)
            {
                return OrganizationErrors.NotFound;
            }

            organization.Archive();
            await db.SaveChangesAsync(cancellationToken);

            return new Result(organization.Id);
        }
    }
}
