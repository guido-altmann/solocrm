using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Import;

/// <summary>
/// Imports organizations from a CSV file (US-16 AK5, iteration 5 decision 15), with the same procedure as the contact
/// import (<see cref="ImportRun"/>). Every organization goes through the validation, audit and <c>OrganizationCreated</c>
/// event of a manual one. Duplicates: first by <c>HubSpotRecordId</c>, otherwise by name (case-insensitive), both against
/// the database and against earlier rows of the same file. Importing organizations before contacts lets the contact
/// import link them by HubSpot company id (decision 16).
/// </summary>
public static class ImportOrganizations
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
                ImportTarget.Organizations,
                "Bitte den Namen einer Spalte zuordnen.",
                ImportField.Organization);
        }
    }

    public sealed class Handler(
        ICrmDbContextFactory dbFactory,
        IValidator<Command> validator,
        IValidator<CreateOrganization.Command> organizationValidator) : ICommandHandler<Command, Result>
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
                db => new Block(db, state, command, organizationValidator),
                command.Progress,
                cancellationToken);
            return new Result(report);
        }
    }

    private sealed class ImportState
    {
        /// <summary>Keys from <see cref="NameKey"/> and <see cref="HubSpotKey"/>.</summary>
        public Dictionary<string, Guid> Organizations { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> SeenInFile { get; } = new(StringComparer.Ordinal);

        public string? LastTagColor { get; set; }

        public static string NameKey(string name) => "name:" + name.Trim().ToUpperInvariant();

        public static string HubSpotKey(string id) => "hubspot:" + id.Trim();

        public static async Task<ImportState> LoadAsync(ICrmDbContextFactory dbFactory, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var state = new ImportState();

            // Active before archived, oldest first: the first entry per name wins (like FindOrganizationByName).
            var organizations = await db.Organizations.AsNoTracking()
                .OrderBy(o => o.IsArchived)
                .ThenBy(o => o.CreatedAt)
                .Select(o => new { o.Id, o.Name, o.ExtraFields })
                .ToListAsync(cancellationToken);
            foreach (var organization in organizations)
            {
                state.Organizations.TryAdd(NameKey(organization.Name), organization.Id);
                if (organization.ExtraFields.TryGetValue(ImportMapping.HubSpotRecordIdKey, out var hubSpotId))
                {
                    state.Organizations.TryAdd(HubSpotKey(hubSpotId), organization.Id);
                }
            }

            state.LastTagColor = await ImportRun.LastTagColorAsync(db, cancellationToken);
            return state;
        }
    }

    private sealed class Block(ICrmDbContext db, ImportState state, Command command, IValidator<CreateOrganization.Command> organizationValidator)
        : IImportBlock
    {
        private readonly Dictionary<string, Guid> _organizations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Tag> _tags = new(StringComparer.OrdinalIgnoreCase);

        public async Task<RowResult> ImportAsync(CsvRow row, CancellationToken cancellationToken)
        {
            var mapping = command.Mapping;
            string? Value(ImportField field) => mapping.Value(row, field);

            var name = Value(ImportField.Organization);
            if (name is null)
            {
                return new RowResult.Failed("Bitte einen Namen angeben.");
            }

            var hubSpotId = Value(ImportField.HubSpotRecordId);
            List<string> keys = hubSpotId is null
                ? [ImportState.NameKey(name)]
                : [ImportState.HubSpotKey(hubSpotId), ImportState.NameKey(name)];
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

            var type = mapping.OrganizationTypeOf(Value(ImportField.OrganizationType));
            var website = Value(ImportField.OrganizationWebsite);

            var (existingId, matchedBy) = FindExisting(name, hubSpotId);
            Organization organization;
            RowResult result;
            if (existingId is { } id)
            {
                if (command.Duplicates == DuplicateHandling.Skip)
                {
                    MarkSeen(keys, row.Number);
                    return new RowResult.Skipped($"Organisation existiert bereits ({matchedBy}).");
                }

                organization = (await db.Organizations.FindAsync([id], cancellationToken))!;
                var merged = new CreateOrganization.Command(
                    name,
                    type ?? organization.Type,
                    website ?? organization.Website,
                    ImportAddress.Merge(address, AddressData.From(organization.Address)),
                    organization.Notes);
                if (await ValidateAsync(merged, cancellationToken) is { } invalid)
                {
                    return invalid;
                }

                organization.Update(merged.Name!, merged.Type, merged.Website, merged.Address!.ToAddress(), merged.Notes);
                result = new RowResult.Updated();
            }
            else
            {
                var create = new CreateOrganization.Command(name, type ?? OrganizationType.Other, website, address);
                if (await ValidateAsync(create, cancellationToken) is { } invalid)
                {
                    return invalid;
                }

                organization = Organization.Create(create.Name!, create.Type, create.Website, address.ToAddress());
                db.Organizations.Add(organization);
                result = new RowResult.Created();
            }

            if (hubSpotId is not null)
            {
                organization.SetExtraField(ImportMapping.HubSpotRecordIdKey, hubSpotId);
            }

            if (tagName is not null)
            {
                await AssignTagAsync(organization.Id, tagName, isNew: result is RowResult.Created, cancellationToken);
            }

            foreach (var key in keys)
            {
                _organizations.TryAdd(key, organization.Id);
            }

            MarkSeen(keys, row.Number);
            return result;
        }

        public void Commit()
        {
            foreach (var (key, id) in _organizations)
            {
                state.Organizations.TryAdd(key, id);
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

        private (Guid? Id, string MatchedBy) FindExisting(string name, string? hubSpotId)
        {
            if (hubSpotId is not null && Find(ImportState.HubSpotKey(hubSpotId)) is { } byHubSpot)
            {
                return (byHubSpot, "HubSpot-ID");
            }

            return Find(ImportState.NameKey(name)) is { } byName ? (byName, "Name") : (null, "");
        }

        private Guid? Find(string key) =>
            state.Organizations.TryGetValue(key, out var id) || _organizations.TryGetValue(key, out id) ? id : null;

        private async Task AssignTagAsync(Guid organizationId, string name, bool isNew, CancellationToken cancellationToken)
        {
            var tag = await ImportRun.ResolveTagAsync(db, _tags, name, () => state.LastTagColor, c => state.LastTagColor = c, cancellationToken);
            if (isNew || !await db.OrganizationTags.AnyAsync(t => t.OrganizationId == organizationId && t.TagId == tag.Id, cancellationToken))
            {
                db.OrganizationTags.Add(new OrganizationTag(organizationId, tag));
            }
        }

        private async Task<RowResult.Failed?> ValidateAsync(CreateOrganization.Command organization, CancellationToken cancellationToken)
        {
            var validation = await organizationValidator.ValidateAsync(organization, cancellationToken);
            return validation.IsValid
                ? null
                : new RowResult.Failed(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()));
        }
    }
}
