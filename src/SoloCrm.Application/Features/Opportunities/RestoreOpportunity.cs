using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Opportunities;

/// <summary>
/// Restores an archived request (US-05 AK2).
/// </summary>
public static class RestoreOpportunity
{
    public sealed record Command(Guid Id);

    public sealed record Result(Guid Id);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var opportunity = await db.Opportunities.SingleOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
            if (opportunity is null)
            {
                return OpportunityErrors.NotFound;
            }

            opportunity.Restore();
            await db.SaveChangesAsync(cancellationToken);

            return new Result(opportunity.Id);
        }
    }
}
