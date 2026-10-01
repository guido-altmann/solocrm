using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Import;

/// <summary>
/// Imports contacts from a CSV file (US-16) in blocks with progress (<see cref="ImportRun"/>, iteration 5 decision 10).
/// Every contact goes through the same validation, audit and <c>ContactCreated</c> event as a manual one (decision 11).
/// <para>Duplicates (AK2): first by email (case-insensitive), otherwise by <c>HubSpotRecordId</c>, both against the
/// database and against earlier rows of the same file.</para>
/// <para>Employer (decision 16): the HubSpot company id links to the organization imported with that id; otherwise the
/// organization name is used (existing one regardless of case, or a new one).</para>
/// </summary>
public static class ImportContacts
{
    public sealed record Command(
        ReadOnlyMemory<byte> Content,
        ImportMapping Mapping,
        DuplicateHandling Duplicates,
        IProgress<ImportProgress>? Progress = null);

    public sealed record Result(ImportReport Report);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Duplicates).IsInEnum().WithMessage("Bitte wählen, wie Dubletten behandelt werden.");
            this.AddMappingRules(
                c => c.Mapping,
                ImportTarget.Contacts,
                "Bitte mindestens Vor- oder Nachname einer Spalte zuordnen.",
                ImportField.FirstName,
                ImportField.LastName);
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

            var prepared = ImportRun.Prepare(command.Content, command.Mapping);
            if (prepared.IsFailure)
            {
                return prepared.Error;
            }

            var state = await ImportState.LoadAsync(dbFactory, cancellationToken);
            var report = await ImportRun.RunAsync(
                prepared.Value,
                dbFactory,
                db => new Block(db, state, command, contactValidator),
                command.Progress,
                cancellationToken);
            return new Result(report);
        }
    }

    /// <summary>What the import knows across blocks: existing contacts and organizations, rows already seen.</summary>
    private sealed class ImportState
    {
        public Dictionary<string, Guid> Contacts { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Guid> OrganizationsByName { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Guid> OrganizationsByHubSpotId { get; } = new(StringComparer.Ordinal);

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
                .Select(o => new { o.Id, o.Name, o.ExtraFields })
                .ToListAsync(cancellationToken);
            foreach (var organization in organizations)
            {
                state.OrganizationsByName.TryAdd(organization.Name.Trim(), organization.Id);
                if (organization.ExtraFields.TryGetValue(ImportMapping.HubSpotRecordIdKey, out var hubSpotId))
                {
                    state.OrganizationsByHubSpotId.TryAdd(hubSpotId.Trim(), organization.Id);
                }
            }

            state.LastTagColor = await ImportRun.LastTagColorAsync(db, cancellationToken);
            return state;
        }
    }

    private sealed class Block(ICrmDbContext db, ImportState state, Command command, IValidator<CreateContact.Command> contactValidator)
        : IImportBlock
    {
        private readonly Dictionary<string, Guid> _contacts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _organizations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Tag> _tags = new(StringComparer.OrdinalIgnoreCase);

        public async Task<RowResult> ImportAsync(CsvRow row, CancellationToken cancellationToken)
        {
            var mapping = command.Mapping;
            string? Value(ImportField field) => mapping.Value(row, field);

            var email = Value(ImportField.Email);
            var hubSpotId = Value(ImportField.HubSpotRecordId);
            var keys = DuplicateKeys(email, hubSpotId);
            if (keys.Select(SeenInFile).FirstOrDefault(r => r is not null) is { } firstRow)
            {
                return new RowResult.Skipped($"Doppelt in der Datei (wie Zeile {firstRow}).");
            }

            var tagName = Value(ImportField.Tag);
            if (tagName is not null && tagName.Length > Tag.NameMaxLength)
            {
                return new RowResult.Failed($"Der Tag darf höchstens {Tag.NameMaxLength} Zeichen lang sein.");
            }

            if (!ImportAddress.TryRead(mapping, row, out var address, out var addressError))
            {
                return addressError;
            }

            var organizationName = Value(ImportField.Organization);
            var companyId = FirstId(Value(ImportField.HubSpotCompanyId));
            var source = mapping.SourceOf(Value(ImportField.Source));
            var linkedInUrl = WithScheme(Value(ImportField.LinkedInUrl));

            var (existingId, matchedBy) = FindExisting(email, hubSpotId);
            Contact contact;
            RowResult result;
            if (existingId is { } id)
            {
                if (command.Duplicates == DuplicateHandling.Skip)
                {
                    MarkSeen(keys, row.Number);
                    return new RowResult.Skipped($"Kontakt existiert bereits ({matchedBy}).");
                }

                contact = (await db.Contacts.FindAsync([id], cancellationToken))!;
                var merged = new CreateContact.Command(
                    Value(ImportField.FirstName) ?? contact.FirstName,
                    Value(ImportField.LastName) ?? contact.LastName,
                    contact.Email ?? email,
                    Value(ImportField.Phone) ?? contact.Phone,
                    Value(ImportField.JobTitle) ?? contact.JobTitle,
                    linkedInUrl ?? contact.LinkedInUrl,
                    null,
                    organizationName,
                    source ?? contact.Source,
                    ImportAddress.Merge(address, AddressData.From(contact.Address)));
                if (await ValidateAsync(merged, cancellationToken) is { } invalid)
                {
                    return invalid;
                }

                var organizationId = ResolveOrganization(companyId, organizationName, Value(ImportField.OrganizationWebsite))
                    ?? contact.OrganizationId;
                contact.Update(merged.FirstName, merged.LastName, merged.Email, merged.Phone, merged.JobTitle, merged.LinkedInUrl, organizationId, merged.Source);
                contact.ChangeAddress(merged.Address!.ToAddress());
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
                    source,
                    address);
                if (await ValidateAsync(create, cancellationToken) is { } invalid)
                {
                    return invalid;
                }

                var organizationId = ResolveOrganization(companyId, organizationName, Value(ImportField.OrganizationWebsite));
                contact = Contact.Create(
                    create.FirstName, create.LastName, create.Email, create.Phone, create.JobTitle, create.LinkedInUrl, organizationId, create.Source,
                    address.ToAddress());
                db.Contacts.Add(contact);
                result = new RowResult.Created();
            }

            if (hubSpotId is not null)
            {
                contact.SetExtraField(ImportMapping.HubSpotRecordIdKey, hubSpotId);
            }

            if (tagName is not null)
            {
                await AssignTagAsync(contact.Id, tagName, isNew: result is RowResult.Created, cancellationToken);
            }

            Register(contact.Id, contact.Email, hubSpotId);
            MarkSeen(keys, row.Number);
            return result;
        }

        public void Commit()
        {
            foreach (var (key, id) in _contacts)
            {
                state.Contacts.TryAdd(key, id);
            }

            foreach (var (key, id) in _organizations)
            {
                state.OrganizationsByName.TryAdd(key, id);
            }

            foreach (var (key, row) in _seen)
            {
                state.SeenInFile.TryAdd(key, row);
            }
        }

        private int? SeenInFile(string key) =>
            state.SeenInFile.TryGetValue(key, out var row) || _seen.TryGetValue(key, out row) ? row : null;

        private void MarkSeen(IEnumerable<string> keys, int row)
        {
            foreach (var key in keys)
            {
                _seen.TryAdd(key, row);
            }
        }

        private (Guid? Id, string MatchedBy) FindExisting(string? email, string? hubSpotId)
        {
            if (email is not null && Find(ImportState.EmailKey(email)) is { } byEmail)
            {
                return (byEmail, "E-Mail");
            }

            return hubSpotId is not null && Find(ImportState.HubSpotKey(hubSpotId)) is { } byHubSpot
                ? (byHubSpot, "HubSpot-ID")
                : (null, "");
        }

        private Guid? Find(string key) =>
            state.Contacts.TryGetValue(key, out var id) || _contacts.TryGetValue(key, out id) ? id : null;

        private void Register(Guid contactId, string? email, string? hubSpotId)
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

        /// <summary>
        /// The organization with the HubSpot company id; otherwise an existing one with this name (case-insensitive) or a
        /// new one of type <c>Other</c>; <c>null</c> when the row names no organization (or only an unknown company id).
        /// </summary>
        private Guid? ResolveOrganization(string? companyId, string? name, string? website)
        {
            if (companyId is not null && state.OrganizationsByHubSpotId.TryGetValue(companyId, out var linked))
            {
                return linked;
            }

            if (name is null)
            {
                return null;
            }

            var key = name.Trim();
            if (state.OrganizationsByName.TryGetValue(key, out var id) || _organizations.TryGetValue(key, out id))
            {
                return id;
            }

            var validWebsite = Website.TryNormalize(website, out var normalized) && normalized.Length <= Organization.WebsiteMaxLength
                ? website
                : null;
            var organization = Organization.Create(key, OrganizationType.Other, validWebsite);
            db.Organizations.Add(organization);
            _organizations[key] = organization.Id;
            return organization.Id;
        }

        private async Task AssignTagAsync(Guid contactId, string name, bool isNew, CancellationToken cancellationToken)
        {
            var tag = await ImportRun.ResolveTagAsync(db, _tags, name, () => state.LastTagColor, c => state.LastTagColor = c, cancellationToken);
            if (isNew || !await db.ContactTags.AnyAsync(t => t.ContactId == contactId && t.TagId == tag.Id, cancellationToken))
            {
                db.ContactTags.Add(new ContactTag(contactId, tag));
            }
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

        /// <summary>HubSpot lists several associated companies separated by <c>;</c>; the first one is the primary.</summary>
        private static string? FirstId(string? ids) =>
            ids?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        /// <summary>HubSpot often exports LinkedIn profiles without scheme (<c>linkedin.com/in/…</c>).</summary>
        private static string? WithScheme(string? url) =>
            url is null || url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
    }
}

/// <summary>Reading and merging the address columns, shared by both imports (decision 14).</summary>
internal static class ImportAddress
{
    /// <summary>Recognizes the country by code, German or English name; an unknown country fails the row.</summary>
    public static bool TryRead(ImportMapping mapping, CsvRow row, out AddressData address, out RowResult.Failed error)
    {
        var countryValue = mapping.Value(row, ImportField.Country);
        var country = Countries.Find(countryValue);
        address = new AddressData(
            mapping.Value(row, ImportField.Street),
            mapping.Value(row, ImportField.Street2),
            mapping.Value(row, ImportField.PostalCode),
            mapping.Value(row, ImportField.City),
            mapping.Value(row, ImportField.Region),
            country?.Code);
        error = new RowResult.Failed($"Unbekanntes Land „{countryValue}“.");
        return countryValue is null || country is not null;
    }

    /// <summary>Like the other fields on update: a value of the file wins, an empty cell keeps the existing value.</summary>
    public static AddressData Merge(AddressData file, AddressData existing) => new(
        file.Street ?? existing.Street,
        file.Street2 ?? existing.Street2,
        file.PostalCode ?? existing.PostalCode,
        file.City ?? existing.City,
        file.Region ?? existing.Region,
        file.CountryCode ?? existing.CountryCode);
}
