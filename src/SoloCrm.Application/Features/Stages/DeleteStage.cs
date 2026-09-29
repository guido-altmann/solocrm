using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Stages;

/// <summary>
/// Deletes a stage. Assigned requests (incl. archived ones) are moved to a target stage with the same status
/// first, each via <c>ChangeStage</c> and thus audited (US-09 AK2). At least one open, one won and one lost
/// stage always remain.
/// </summary>
public static class DeleteStage
{
    public sealed record Command(Guid Id, Guid? TargetStageId = null);

    public sealed record Result(int ReassignedCount);

    public sealed class Handler(ICrmDbContextFactory dbFactory, TimeProvider timeProvider) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var stages = await db.Stages.ToListAsync(cancellationToken);
            var stage = stages.SingleOrDefault(s => s.Id == command.Id);
            if (stage is null)
            {
                return StageErrors.NotFound;
            }

            if (stages.Count(s => s.Status == stage.Status) == 1)
            {
                return StageErrors.LastOfStatus(stage.Status);
            }

            var opportunities = await db.Opportunities.Where(o => o.StageId == stage.Id).ToListAsync(cancellationToken);
            if (opportunities.Count > 0)
            {
                if (command.TargetStageId is not { } targetId)
                {
                    return StageErrors.TargetRequired;
                }

                var target = stages.SingleOrDefault(s => s.Id == targetId);
                if (target is null || target.Id == stage.Id || target.Status != stage.Status)
                {
                    return StageErrors.InvalidTarget;
                }

                var now = timeProvider.GetUtcNow();
                foreach (var opportunity in opportunities)
                {
                    opportunity.ChangeStage(target, null, now);
                }
            }

            db.Stages.Remove(stage);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(opportunities.Count);
        }
    }
}
