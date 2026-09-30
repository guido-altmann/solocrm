using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Tags;

/// <summary>Removes a tag from a contact, organization or request (US-15). Removing a missing assignment is not an error.</summary>
public static class RemoveTag
{
    public sealed record Command(TimelineRecordType RecordType, Guid RecordId, Guid TagId);

    public sealed record Result(Guid TagId, bool Removed);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.RecordType).IsInEnum().WithMessage("Unbekannter Datensatztyp.");
        }
    }

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

            // Loaded with the tag, so the audit entry names it.
            ITagAssignment? assignment = command.RecordType switch
            {
                TimelineRecordType.Contact => await db.ContactTags.Include(t => t.Tag)
                    .SingleOrDefaultAsync(t => t.ContactId == command.RecordId && t.TagId == command.TagId, cancellationToken),
                TimelineRecordType.Organization => await db.OrganizationTags.Include(t => t.Tag)
                    .SingleOrDefaultAsync(t => t.OrganizationId == command.RecordId && t.TagId == command.TagId, cancellationToken),
                _ => await db.OpportunityTags.Include(t => t.Tag)
                    .SingleOrDefaultAsync(t => t.OpportunityId == command.RecordId && t.TagId == command.TagId, cancellationToken),
            };

            if (assignment is null)
            {
                return new Result(command.TagId, false);
            }

            switch (assignment)
            {
                case ContactTag contactTag:
                    db.ContactTags.Remove(contactTag);
                    break;
                case OrganizationTag organizationTag:
                    db.OrganizationTags.Remove(organizationTag);
                    break;
                case OpportunityTag opportunityTag:
                    db.OpportunityTags.Remove(opportunityTag);
                    break;
            }

            await db.SaveChangesAsync(cancellationToken);
            return new Result(command.TagId, true);
        }
    }
}
