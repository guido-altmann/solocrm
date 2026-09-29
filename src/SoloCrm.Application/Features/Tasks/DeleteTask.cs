using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Tasks;

/// <summary>Hard-deletes a task (e.g. created by mistake); the audit interceptor records it as <c>Deleted</c>.</summary>
public static class DeleteTask
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

            db.Tasks.Remove(task);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(task.Id);
        }
    }
}
