using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Tasks;

/// <summary>
/// Completes a task right away, incl. <c>TaskCompleted</c> in the outbox; the UI offers an undo via
/// <see cref="ReopenTask"/> (US-11 AK2, iteration 3 decision 2).
/// </summary>
public static class CompleteTask
{
    public sealed record Command(Guid Id);

    public sealed record Result(Guid Id, DateTimeOffset CompletedAt);

    public sealed class Handler(ICrmDbContextFactory dbFactory, TimeProvider timeProvider) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
            if (task is null)
            {
                return TaskErrors.NotFound;
            }

            task.Complete(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);

            return new Result(task.Id, task.CompletedAt!.Value);
        }
    }
}
