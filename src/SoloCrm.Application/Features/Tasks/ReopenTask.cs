using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Tasks;

/// <summary>
/// Opens a completed task again („Rückgängig“ after completing, US-11 AK2); raises <c>TaskReopened</c>.
/// </summary>
public static class ReopenTask
{
    public sealed record Command(Guid Id);

    public sealed record Result(Guid Id);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
            if (task is null)
            {
                return TaskErrors.NotFound;
            }

            task.Reopen();
            await db.SaveChangesAsync(cancellationToken);

            return new Result(task.Id);
        }
    }
}
