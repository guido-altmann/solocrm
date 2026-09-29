using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Pipeline;

/// <summary>
/// Moves a request to another stage (drag &amp; drop). The change is audited and raises
/// <c>OpportunityStageChanged</c> (US-07 AK1); a lost stage requires a reason (US-08 AK1).
/// </summary>
public static class MoveOpportunity
{
    public sealed record Command(Guid OpportunityId, Guid StageId, LostReason? LostReason = null);

    public sealed record Result(Guid Id, StageStatus Status);

    public sealed class Handler(ICrmDbContextFactory dbFactory, TimeProvider timeProvider) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            if (command.LostReason is { } reason && !Enum.IsDefined(reason))
            {
                return new ValidationError(new Dictionary<string, string[]>
                {
                    [nameof(Command.LostReason)] = ["Bitte einen gültigen Absagegrund wählen."],
                });
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var opportunity = await db.Opportunities.SingleOrDefaultAsync(o => o.Id == command.OpportunityId, cancellationToken);
            if (opportunity is null)
            {
                return OpportunityErrors.NotFound;
            }

            var stage = await db.Stages.SingleOrDefaultAsync(s => s.Id == command.StageId, cancellationToken);
            if (stage is null)
            {
                return OpportunityErrors.StageNotFound;
            }

            if (command.LostReason is null && opportunity.RequiresLostReason(stage))
            {
                return OpportunityErrors.LostReasonRequired;
            }

            opportunity.ChangeStage(stage, command.LostReason, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);

            return new Result(opportunity.Id, opportunity.Status);
        }
    }
}
