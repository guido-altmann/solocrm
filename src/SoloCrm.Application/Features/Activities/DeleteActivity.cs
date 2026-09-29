using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Activities;

/// <summary>
/// Hard-deletes an activity; the audit interceptor records it as <c>Deleted</c> (iteration 3, decision 3).
/// </summary>
public static class DeleteActivity
{
    public sealed record Command(Guid Id);

    public sealed record Result(Guid Id);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var activity = await db.Activities.SingleOrDefaultAsync(a => a.Id == command.Id, cancellationToken);
            if (activity is null)
            {
                return ActivityErrors.NotFound;
            }

            db.Activities.Remove(activity);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(activity.Id);
        }
    }
}
