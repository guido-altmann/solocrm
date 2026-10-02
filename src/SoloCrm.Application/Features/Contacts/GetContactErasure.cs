using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// What the GDPR erasure of a contact affects (US-20), shown in the confirmation dialog: the name to type, the
/// activities and tasks deleted with it and the opportunities that lose their primary contact.
/// </summary>
public static class GetContactErasure
{
    public sealed record Query(Guid ContactId);

    public sealed record Result(Guid ContactId, string Name, int ActivityCount, int TaskCount, IReadOnlyList<RecordRef> Opportunities);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var id = query.ContactId;

            var contact = await db.Contacts
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new { c.FirstName, c.LastName })
                .SingleOrDefaultAsync(cancellationToken);
            if (contact is null)
            {
                return ContactErrors.NotFound;
            }

            var activityCount = await db.Activities.CountAsync(a => a.ContactId == id, cancellationToken);
            var taskCount = await db.Tasks.CountAsync(t => t.ContactId == id, cancellationToken);
            var opportunities = await db.Opportunities
                .AsNoTracking()
                .Where(o => o.PrimaryContactId == id)
                .OrderBy(o => o.Title)
                .Select(o => new RecordRef(o.Id, o.Title))
                .ToListAsync(cancellationToken);

            return new Result(id, Names.Person(contact.FirstName, contact.LastName), activityCount, taskCount, opportunities);
        }
    }
}
