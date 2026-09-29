using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Stages;

/// <summary>
/// Applies a new pipeline order; the list must contain every stage exactly once (US-09 AK1).
/// </summary>
public static class ReorderStages
{
    public sealed record Command(IReadOnlyList<Guid> OrderedIds);

    public sealed record Result(int Count);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var stages = await db.Stages.ToDictionaryAsync(s => s.Id, cancellationToken);

            if (command.OrderedIds.Count != stages.Count
                || command.OrderedIds.Distinct().Count() != stages.Count
                || !command.OrderedIds.All(stages.ContainsKey))
            {
                return StageErrors.IncompleteOrder;
            }

            for (var i = 0; i < command.OrderedIds.Count; i++)
            {
                stages[command.OrderedIds[i]].MoveTo(i + 1);
            }

            await db.SaveChangesAsync(cancellationToken);

            return new Result(stages.Count);
        }
    }
}
