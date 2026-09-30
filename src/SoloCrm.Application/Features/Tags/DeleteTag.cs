using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Tags;

/// <summary>
/// Deletes a tag together with its assignments (US-15). The assignments are removed explicitly instead of relying on
/// <c>ON DELETE CASCADE</c>, so that every tagged record gets its audit entry (iteration 4 decision 3).
/// </summary>
public static class DeleteTag
{
    public sealed record Command(Guid Id);

    public sealed record Result(Guid Id, int RemovedAssignments);

    public sealed class Validator : AbstractValidator<Command>;

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
            if (tag is null)
            {
                return TagErrors.NotFound;
            }

            var contacts = await db.ContactTags.Where(t => t.TagId == tag.Id).ToListAsync(cancellationToken);
            var organizations = await db.OrganizationTags.Where(t => t.TagId == tag.Id).ToListAsync(cancellationToken);
            var opportunities = await db.OpportunityTags.Where(t => t.TagId == tag.Id).ToListAsync(cancellationToken);

            db.ContactTags.RemoveRange(contacts);
            db.OrganizationTags.RemoveRange(organizations);
            db.OpportunityTags.RemoveRange(opportunities);
            db.Tags.Remove(tag);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(tag.Id, contacts.Count + organizations.Count + opportunities.Count);
        }
    }
}
