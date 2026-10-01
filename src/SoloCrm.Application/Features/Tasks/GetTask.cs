using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Tasks;

/// <summary>Loads one task with its links (REST API, SPEC 5).</summary>
public static class GetTask
{
    public sealed record Query(Guid Id);

    public sealed record Result(
        Guid Id,
        string Title,
        DateOnly? DueDate,
        DateTimeOffset? CompletedAt,
        Guid? ContactId,
        Guid? OrganizationId,
        Guid? OpportunityId,
        DateTimeOffset CreatedAt);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var task = await db.Tasks.AsNoTracking()
                .Where(t => t.Id == query.Id)
                .Select(t => new Result(t.Id, t.Title, t.DueDate, t.CompletedAt, t.ContactId, t.OrganizationId, t.OpportunityId, t.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken);

            return task is null ? TaskErrors.NotFound : task;
        }
    }
}
