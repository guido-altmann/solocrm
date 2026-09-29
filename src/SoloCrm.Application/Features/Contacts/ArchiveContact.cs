using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// Archives a contact; it disappears from lists and search (US-05 AK1).
/// </summary>
public static class ArchiveContact
{
    public sealed record Command(Guid Id);

    public sealed record Result(Guid Id);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
            if (contact is null)
            {
                return ContactErrors.NotFound;
            }

            contact.Archive();
            await db.SaveChangesAsync(cancellationToken);

            return new Result(contact.Id);
        }
    }
}
