using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Import;

/// <summary>
/// Imports contacts from a CSV file (US-16). Runs synchronously in blocks of <see cref="BlockSize"/> rows, one
/// transaction per block, and reports progress after each block (iteration 5 decision 10). Every contact goes through
/// the same validation, audit and <c>ContactCreated</c> event as a manual one (decision 11).
/// <para>Duplicates (AK2): first by email (case-insensitive), otherwise by <c>HubSpotRecordId</c>, both against the
/// database and against earlier rows of the same file.</para>
/// </summary>
public static class ImportContacts
{
    public const int BlockSize = 100;

    public sealed record Command(
        ReadOnlyMemory<byte> Content,
        ImportMapping Mapping,
        DuplicateHandling Duplicates,
        IProgress<Progress>? Progress = null);

    public sealed record Progress(int Processed, int Total);

    /// <param name="Row">Row number in the file (header = 1).</param>
    public sealed record RowIssue(int Row, string Reason);

    public sealed record Result(int Created, int Updated, int Skipped, int Failed, IReadOnlyList<RowIssue> Skips, IReadOnlyList<RowIssue> Errors);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Duplicates).IsInEnum().WithMessage("Bitte wählen, wie Dubletten behandelt werden.");
            RuleFor(c => c.Mapping).NotNull().WithMessage("Bitte die Spalten zuordnen.");
            RuleFor(c => c.Mapping.Template).IsInEnum().WithMessage("Unbekannte Vorlage.").When(c => c.Mapping is not null);
            RuleFor(c => c.Mapping.Fields)
                .Must(fields => fields.Any(f => f.Column is not null && f.Field is ImportField.FirstName or ImportField.LastName))
                .WithMessage("Bitte mindestens Vor- oder Nachname einer Spalte zuordnen.")
                .Must(fields => fields.GroupBy(f => f.Field).All(g => g.Count() == 1))
                .WithMessage("Jedes Zielfeld darf nur einmal zugeordnet werden.")
                .Must(fields => fields.All(f => f.Column is null or >= 0 && f.FallbackColumn is null or >= 0))
                .WithMessage("Ungültige Spalte.")
                .OverridePropertyName(nameof(Command.Mapping))
                .When(c => c.Mapping is not null);
        }
    }

    public sealed class Handler(
        ICrmDbContextFactory dbFactory,
        IValidator<Command> validator,
        IValidator<CreateContact.Command> contactValidator) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            var parsed = CsvDocument.Parse(command.Content);
            if (parsed.IsFailure)
            {
                return parsed.Error;
            }

            var document = parsed.Value;
            var columnCount = document.Headers.Count;
            if (command.Mapping.Fields.Any(f => f.Column >= columnCount || f.FallbackColumn >= columnCount))
            {
                return new ValidationError(new Dictionary<string, string[]>
                {
                    [nameof(Command.Mapping)] = ["Eine zugeordnete Spalte existiert in der Datei nicht."],
                });
            }

            var state = await ImportState.LoadAsync(dbFactory, cancellationToken);
            var outcome = new Outcome();
            var processed = 0;
            foreach (var block in document.Rows.Chunk(BlockSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ImportBlockAsync(block, command, state, outcome, cancellationToken);
                processed += block.Length;
                command.Progress?.Report(new Progress(processed, document.Rows.Count));
            }

            return new Result(
                outcome.Created,
                outcome.Updated,
                outcome.Skips.Count,
                outcome.Errors.Count,
                [.. outcome.Skips.OrderBy(i => i.Row)],
                [.. outcome.Errors.OrderBy(i => i.Row)]);
        }

        private async Task ImportBlockAsync(CsvRow[] rows, Command command, ImportState state, Outcome outcome, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var block = new Block(db, state);
            var created = new List<int>();
            var updated = new List<int>();
            var skips = new List<RowIssue>();

            foreach (var row in rows)
            {
                var result = await ImportRowAsync(row, command, block, cancellationToken);
                switch (result)
                {
                    case RowResult.Created:
                        created.Add(row.Number);
                        break;
                    case RowResult.Updated:
                        updated.Add(row.Number);
                        break;
                    case RowResult.Skipped skipped:
                        skips.Add(new RowIssue(row.Number, skipped.Reason));
                        break;
                    case RowResult.Failed failed:
                        outcome.Errors.Add(new RowIssue(row.Number, failed.Reason));
                        break;
                }
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // E.g. a contact with the same email was created in the UI meanwhile; the unique index protects the data.
                var reason = $"Speichern fehlgeschlagen; die Zeilen {rows[0].Number}–{rows[^1].Number} wurden nicht übernommen.";
                outcome.Errors.AddRange(created.Concat(updated).Select(number => new RowIssue(number, reason)));
                outcome.Skips.AddRange(skips);
                return;
            }

            block.Commit();
            outcome.Created += created.Count;
            outcome.Updated += updated.Count;
            outcome.Skips.AddRange(skips);
        }

        private async Task<RowResult> ImportRowAsync(CsvRow row, Command command, Block block, CancellationToken cancellationToken)
        {
            var mapping = command.Mapping;
            string? Value(ImportField field) => mapping.Value(row, field);

            var email = Value(ImportField.Email);
            var hubSpotId = Value(ImportField.HubSpotRecordId);
            var keys = DuplicateKeys(email, hubSpotId);
            if (keys.Select(block.SeenInFile).FirstOrDefault(r => r is not null) is { } firstRow)
            {
                return new RowResult.Skipped($"Doppelt in der Datei (wie Zeile {firstRow}).");
            }

            var tagName = Value(ImportField.Tag);
            if (tagName is not null && tagName.Length > Tag.NameMaxLength)
            {
                return new RowResult.Failed($"Der Tag darf höchstens {Tag.NameMaxLength} Zeichen lang sein.");
            }

            var organizationName = Value(ImportField.Organization);
            var source = mapping.SourceOf(Value(ImportField.Source));
            var linkedInUrl = WithScheme(Value(ImportField.LinkedInUrl));

            var (existingId, matchedBy) = block.FindExisting(email, hubSpotId);
            Contact contact;
            RowResult result;
            if (existingId is { } id)
            {
                if (command.Duplicates == DuplicateHandling.Skip)
                {
                    block.MarkSeen(keys, row.Number);
                    return new RowResult.Skipped($"Kontakt existiert bereits ({matchedBy}).");
                }

                contact = (await block.Db.Contacts.FindAsync([id], cancellationToken))!;
                var merged = new CreateContact.Command(
                    Value(ImportField.FirstName) ?? contact.FirstName,
                    Value(ImportField.LastName) ?? contact.LastName,
                    contact.Email ?? email,
                    Value(ImportField.Phone) ?? contact.Phone,
                    Value(ImportField.JobTitle) ?? contact.JobTitle,
                    linkedInUrl ?? contact.LinkedInUrl,
                    null,
                    organizationName,
                    source ?? contact.Source);
                if (await ValidateAsync(merged, cancellationToken) is { } invalid)
                {
                    return invalid;
                }

                var organizationId = organizationName is null
                    ? contact.OrganizationId
                    : block.ResolveOrganization(organizationName, Value(ImportField.OrganizationWebsite));
                contact.Update(merged.FirstName, merged.LastName, merged.Email, merged.Phone, merged.JobTitle, merged.LinkedInUrl, organizationId, merged.Source);
                result = new RowResult.Updated();
            }
            else
            {
                var create = new CreateContact.Command(
                    Value(ImportField.FirstName),
                    Value(ImportField.LastName),
                    email,
                    Value(ImportField.Phone),
                    Value(ImportField.JobTitle),
                    linkedInUrl,
                    null,
                    organizationName,
                    source);
                if (await ValidateAsync(create, cancellationToken) is { } invalid)
                {
                    return invalid;
                }

                Guid? organizationId = organizationName is null
                    ? null
                    : block.ResolveOrganization(organizationName, Value(ImportField.OrganizationWebsite));
                contact = Contact.Create(create.FirstName, create.LastName, create.Email, create.Phone, create.JobTitle, create.LinkedInUrl, organizationId, create.Source);
                block.Db.Contacts.Add(contact);
                result = new RowResult.Created();
            }

            if (hubSpotId is not null)
            {
                contact.SetExtraField(ImportMapping.HubSpotRecordIdKey, hubSpotId);
            }

            if (tagName is not null)
            {
                await block.AssignTagAsync(contact.Id, tagName, isNew: result is RowResult.Created, cancellationToken);
            }

            block.Register(contact.Id, contact.Email, hubSpotId);
            block.MarkSeen(keys, row.Number);
            return result;
        }

        private async Task<RowResult.Failed?> ValidateAsync(CreateContact.Command contact, CancellationToken cancellationToken)
        {
            var validation = await contactValidator.ValidateAsync(contact, cancellationToken);
            return validation.IsValid
                ? null
                : new RowResult.Failed(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()));
        }

        private static List<string> DuplicateKeys(string? email, string? hubSpotId)
        {
            var keys = new List<string>(2);
            if (email is not null)
            {
                keys.Add(ImportState.EmailKey(email));
            }

            if (hubSpotId is not null)
            {
                keys.Add(ImportState.HubSpotKey(hubSpotId));
            }

            return keys;
        }

        /// <summary>HubSpot often exports LinkedIn profiles without scheme (<c>linkedin.com/in/…</c>).</summary>
        private static string? WithScheme(string? url) =>
            url is null || url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
    }

    private abstract record RowResult
    {
        public sealed record Created : RowResult;

        public sealed record Updated : RowResult;

        public sealed record Skipped(string Reason) : RowResult;

        public sealed record Failed(string Reason) : RowResult;
    }

    private sealed class Outcome
    {
        public int Created { get; set; }

        public int Updated { get; set; }

        public List<RowIssue> Skips { get; } = [];

        public List<RowIssue> Errors { get; } = [];
    }

    /// <summary>What the import knows across blocks: existing contacts, organizations and rows already seen.</summary>
    private sealed class ImportState
    {
        public Dictionary<string, Guid> Contacts { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Guid> Organizations { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> SeenInFile { get; } = new(StringComparer.Ordinal);

        public string? LastTagColor { get; set; }

        public static string EmailKey(string email) => "email:" + email.Trim().ToUpperInvariant();

        public static string HubSpotKey(string id) => "hubspot:" + id.Trim();

        /// <summary>
        /// Loads the lookup data once (also archived records: emails are unique across all contacts). A few
        /// thousand rows fit in memory and save one query per CSV row.
        /// </summary>
        public static async Task<ImportState> LoadAsync(ICrmDbContextFactory dbFactory, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var state = new ImportState();

            var contacts = await db.Contacts.AsNoTracking()
                .Select(c => new { c.Id, c.Email, c.ExtraFields })
                .ToListAsync(cancellationToken);
            foreach (var contact in contacts)
            {
                if (contact.Email is not null)
                {
                    state.Contacts.TryAdd(EmailKey(contact.Email), contact.Id);
                }

                if (contact.ExtraFields.TryGetValue(ImportMapping.HubSpotRecordIdKey, out var hubSpotId))
                {
                    state.Contacts.TryAdd(HubSpotKey(hubSpotId), contact.Id);
                }
            }

            // Active before archived, oldest first: the first entry per name wins (like FindOrganizationByName).
            var organizations = await db.Organizations.AsNoTracking()
                .OrderBy(o => o.IsArchived)
                .ThenBy(o => o.CreatedAt)
                .Select(o => new { o.Id, o.Name })
                .ToListAsync(cancellationToken);
            foreach (var organization in organizations)
            {
                state.Organizations.TryAdd(organization.Name.Trim(), organization.Id);
            }

            state.LastTagColor = await db.Tags
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Select(t => t.Color)
                .FirstOrDefaultAsync(cancellationToken);
            return state;
        }
    }

    /// <summary>
    /// Changes of one block. Lookup entries become visible to later blocks only after the block was saved
    /// (<see cref="Commit"/>), so that a failed block leaves no dangling references.
    /// </summary>
    private sealed class Block(ICrmDbContext db, ImportState state)
    {
        private readonly Dictionary<string, Guid> _contacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _organizations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Tag> _tags = new(StringComparer.OrdinalIgnoreCase);

        public ICrmDbContext Db { get; } = db;

        public int? SeenInFile(string key) =>
            state.SeenInFile.TryGetValue(key, out var row) || _seen.TryGetValue(key, out row) ? row : null;

        public void MarkSeen(IEnumerable<string> keys, int row)
        {
            foreach (var key in keys)
            {
                _seen.TryAdd(key, row);
            }
        }

        public (Guid? Id, string MatchedBy) FindExisting(string? email, string? hubSpotId)
        {
            if (email is not null && Find(ImportState.EmailKey(email)) is { } byEmail)
            {
                return (byEmail, "E-Mail");
            }

            return hubSpotId is not null && Find(ImportState.HubSpotKey(hubSpotId)) is { } byHubSpot
                ? (byHubSpot, "HubSpot-ID")
                : (null, "");
        }

        public void Register(Guid contactId, string? email, string? hubSpotId)
        {
            if (email is not null)
            {
                _contacts.TryAdd(ImportState.EmailKey(email), contactId);
            }

            if (hubSpotId is not null)
            {
                _contacts.TryAdd(ImportState.HubSpotKey(hubSpotId), contactId);
            }
        }

        /// <summary>An existing organization with this name (case-insensitive) or a new one of type <c>Other</c>.</summary>
        public Guid ResolveOrganization(string name, string? website)
        {
            var key = name.Trim();
            if (state.Organizations.TryGetValue(key, out var id) || _organizations.TryGetValue(key, out id))
            {
                return id;
            }

            var validWebsite = Website.TryNormalize(website, out var normalized) && normalized.Length <= Organization.WebsiteMaxLength
                ? website
                : null;
            var organization = Organization.Create(key, OrganizationType.Other, validWebsite);
            Db.Organizations.Add(organization);
            _organizations[key] = organization.Id;
            return organization.Id;
        }

        public async Task AssignTagAsync(Guid contactId, string name, bool isNew, CancellationToken cancellationToken)
        {
            var normalized = Tag.NormalizeName(name);
            if (!_tags.TryGetValue(normalized, out var tag))
            {
                // The name column is citext, so this lookup ignores case.
                tag = await Db.Tags.FirstOrDefaultAsync(t => t.Name == normalized, cancellationToken);
                if (tag is null)
                {
                    var color = TagPalette.Next(state.LastTagColor);
                    tag = Tag.Create(normalized, color);
                    Db.Tags.Add(tag);
                    state.LastTagColor = color;
                }

                _tags[normalized] = tag;
            }

            if (isNew || !await Db.ContactTags.AnyAsync(t => t.ContactId == contactId && t.TagId == tag.Id, cancellationToken))
            {
                Db.ContactTags.Add(new ContactTag(contactId, tag));
            }
        }

        public void Commit()
        {
            foreach (var (key, id) in _contacts)
            {
                state.Contacts.TryAdd(key, id);
            }

            foreach (var (key, id) in _organizations)
            {
                state.Organizations.TryAdd(key, id);
            }

            foreach (var (key, row) in _seen)
            {
                state.SeenInFile.TryAdd(key, row);
            }
        }

        private Guid? Find(string key) =>
            state.Contacts.TryGetValue(key, out var id) || _contacts.TryGetValue(key, out id) ? id : null;
    }
}
