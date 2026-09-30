using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Tags;

/// <summary>
/// Assigns an existing tag, or one created inline („Neu anlegen: …“), to a contact, organization or request
/// (US-15 AK1). A new name that matches an existing tag regardless of case reuses that tag. Assigning twice is
/// not an error.
/// </summary>
public static class AssignTag
{
    public sealed record Command(TimelineRecordType RecordType, Guid RecordId, Guid? TagId = null, string? NewTagName = null);

    public sealed record Result(Guid TagId, string Name, string Color);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.RecordType).IsInEnum().WithMessage("Unbekannter Datensatztyp.");

            RuleFor(c => c.NewTagName)
                .Must((command, name) => command.TagId is null != string.IsNullOrWhiteSpace(name))
                .WithMessage("Bitte entweder einen bestehenden Tag wählen oder einen neuen anlegen.");

            RuleFor(c => c.NewTagName).ValidTagName().When(c => c.TagId is null);
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

            if (!await TagRules.RecordExistsAsync(db, command.RecordType, command.RecordId, cancellationToken))
            {
                return TagErrors.RecordNotFound;
            }

            var tag = await ResolveTagAsync(db, command, cancellationToken);
            if (tag is null)
            {
                return TagErrors.NotFound;
            }

            if (!await IsAssignedAsync(db, command.RecordType, command.RecordId, tag.Id, cancellationToken))
            {
                Add(db, command.RecordType, command.RecordId, tag);
                await db.SaveChangesAsync(cancellationToken);
            }

            return new Result(tag.Id, tag.Name, tag.Color);
        }

        /// <summary>The chosen tag, or the tag named <see cref="Command.NewTagName"/>, created if it does not exist yet.</summary>
        private static async Task<Tag?> ResolveTagAsync(ICrmDbContext db, Command command, CancellationToken cancellationToken)
        {
            if (command.TagId is { } tagId)
            {
                return await db.Tags.SingleOrDefaultAsync(t => t.Id == tagId, cancellationToken);
            }

            var name = Tag.NormalizeName(command.NewTagName!);
            var existing = await db.Tags.SingleOrDefaultAsync(t => t.Name == name, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var tag = Tag.Create(name, await TagRules.NextColorAsync(db, cancellationToken));
            db.Tags.Add(tag);
            return tag;
        }

        private static Task<bool> IsAssignedAsync(ICrmDbContext db, TimelineRecordType type, Guid recordId, Guid tagId, CancellationToken cancellationToken) =>
            type switch
            {
                TimelineRecordType.Contact => db.ContactTags.AnyAsync(t => t.ContactId == recordId && t.TagId == tagId, cancellationToken),
                TimelineRecordType.Organization => db.OrganizationTags.AnyAsync(t => t.OrganizationId == recordId && t.TagId == tagId, cancellationToken),
                _ => db.OpportunityTags.AnyAsync(t => t.OpportunityId == recordId && t.TagId == tagId, cancellationToken),
            };

        private static void Add(ICrmDbContext db, TimelineRecordType type, Guid recordId, Tag tag)
        {
            switch (type)
            {
                case TimelineRecordType.Contact:
                    db.ContactTags.Add(new ContactTag(recordId, tag));
                    break;
                case TimelineRecordType.Organization:
                    db.OrganizationTags.Add(new OrganizationTag(recordId, tag));
                    break;
                default:
                    db.OpportunityTags.Add(new OpportunityTag(recordId, tag));
                    break;
            }
        }
    }
}
