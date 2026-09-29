using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Tasks;

/// <summary>
/// Open tasks linked directly to one record (detail view). Completed tasks appear in the timeline instead.
/// </summary>
public static class GetTasks
{
    public sealed record Query(Guid? ContactId = null, Guid? OrganizationId = null, Guid? OpportunityId = null);

    public sealed record Result(IReadOnlyList<TaskSummary> Items);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var tasks = db.Tasks.AsNoTracking().Where(t => t.CompletedAt == null);
            if (query.ContactId is { } contactId)
            {
                tasks = tasks.Where(t => t.ContactId == contactId);
            }

            if (query.OrganizationId is { } organizationId)
            {
                tasks = tasks.Where(t => t.OrganizationId == organizationId);
            }

            if (query.OpportunityId is { } opportunityId)
            {
                tasks = tasks.Where(t => t.OpportunityId == opportunityId);
            }

            return new Result(await tasks.ToSummaries().ToListAsync(cancellationToken));
        }
    }
}
