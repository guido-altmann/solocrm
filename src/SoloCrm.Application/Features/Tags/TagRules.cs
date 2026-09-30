using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Tags;

/// <summary>A tag as shown on a chip.</summary>
public sealed record TagRef(Guid Id, string Name, string Color);

public static class TagErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Tag.NotFound", "Der Tag wurde nicht gefunden.");

    public static Error DuplicateName { get; } =
        Error.Conflict("Tag.DuplicateName", "Ein Tag mit diesem Namen existiert bereits.");

    public static Error RecordNotFound { get; } =
        Error.NotFound("Tag.RecordNotFound", "Der Datensatz wurde nicht gefunden.");
}

internal static class TagValidation
{
    /// <summary>The length counts after normalization (<see cref="Tag.NormalizeName"/>), like the entity does.</summary>
    public static IRuleBuilderOptions<T, string?> ValidTagName<T>(this IRuleBuilder<T, string?> rule) =>
        rule
            .NotEmpty()
            .WithMessage("Bitte einen Namen angeben.")
            .Must(name => name is null || NormalizedLength(name) <= Tag.NameMaxLength)
            .WithMessage($"Der Name darf höchstens {Tag.NameMaxLength} Zeichen lang sein.");

    private static int NormalizedLength(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Length;
}

internal static class TagRules
{
    /// <summary>
    /// The name column is case-insensitive (citext), so this comparison is too.
    /// The unique index remains the safety net for concurrent writes.
    /// </summary>
    public static Task<bool> IsNameTakenAsync(ICrmDbContext db, string normalizedName, Guid? exceptTagId, CancellationToken cancellationToken) =>
        db.Tags.AnyAsync(t => t.Name == normalizedName && t.Id != exceptTagId, cancellationToken);

    /// <summary>New tags get the palette colors in turn (iteration 4 decision 7).</summary>
    public static async Task<string> NextColorAsync(ICrmDbContext db, CancellationToken cancellationToken)
    {
        var previous = await db.Tags
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Select(t => t.Color)
            .FirstOrDefaultAsync(cancellationToken);
        return TagPalette.Next(previous);
    }

    public static Task<bool> RecordExistsAsync(ICrmDbContext db, TimelineRecordType type, Guid id, CancellationToken cancellationToken) =>
        type switch
        {
            TimelineRecordType.Contact => db.Contacts.AnyAsync(c => c.Id == id, cancellationToken),
            TimelineRecordType.Organization => db.Organizations.AnyAsync(o => o.Id == id, cancellationToken),
            _ => db.Opportunities.AnyAsync(o => o.Id == id, cancellationToken),
        };

    /// <summary>Tags of a record, ordered by name.</summary>
    public static IQueryable<TagRef> TagsOf(ICrmDbContext db, TimelineRecordType type, Guid id) =>
        (type switch
        {
            TimelineRecordType.Contact => db.ContactTags.Where(t => t.ContactId == id).Select(t => t.Tag!),
            TimelineRecordType.Organization => db.OrganizationTags.Where(t => t.OrganizationId == id).Select(t => t.Tag!),
            _ => db.OpportunityTags.Where(t => t.OpportunityId == id).Select(t => t.Tag!),
        })
        .OrderBy(t => t.Name)
        .Select(t => new TagRef(t.Id, t.Name, t.Color));
}
