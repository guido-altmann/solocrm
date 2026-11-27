using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Enrichment;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Enrichment;

/// <summary>
/// Applies one suggestion to the organization (ADR-016): address, phone and e-mail through the regular fields, legal
/// form, register, managing directors and VAT id as extra fields, the company profile as a note with the origin
/// <see cref="ActivityOrigin.Enrichment"/>. The accepted suggestion remains as provenance (source, time, confidence).
/// </summary>
public static class AcceptSuggestion
{
    public sealed record Command(Guid SuggestionId);

    /// <param name="ActivityId">The new note; only for the company profile.</param>
    public sealed record Result(Guid SuggestionId, Guid OrganizationId, Guid? ActivityId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.SuggestionId).NotEmpty().WithMessage("Der Vorschlag fehlt.");
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator, AppClock clock)
        : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var suggestion = await db.EnrichmentSuggestions.SingleOrDefaultAsync(s => s.Id == command.SuggestionId, cancellationToken);
            if (suggestion is null)
            {
                return EnrichmentSuggestionErrors.NotFound;
            }

            if (!suggestion.IsOpen)
            {
                return EnrichmentSuggestionErrors.NotOpen(suggestion.Status);
            }

            var organization = await db.Organizations
                .Where(o => db.EnrichmentRequests.Any(r => r.Id == suggestion.RequestId
                    && r.TargetType == EnrichmentTargetType.Organization
                    && r.TargetId == o.Id))
                .SingleOrDefaultAsync(cancellationToken);
            if (organization is null)
            {
                return OrganizationErrors.NotFound;
            }

            if (organization.IsArchived)
            {
                return EnrichmentSuggestionErrors.Archived;
            }

            Guid? activityId = null;
            try
            {
                if (suggestion.Field == EnrichmentField.CompanyProfile)
                {
                    var note = Activity.LogEnrichment(
                        clock.UtcNow,
                        EnrichmentValues.Label(suggestion.Field),
                        ProfileBody(suggestion),
                        new LinkedRecords(null, organization.Id, null));
                    db.Activities.Add(note);
                    activityId = note.Id;
                }
                else if (!TryApply(organization, suggestion))
                {
                    return EnrichmentSuggestionErrors.InvalidValue;
                }
            }
            catch (ArgumentException)
            {
                return EnrichmentSuggestionErrors.InvalidValue;
            }

            suggestion.Accept(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(suggestion.Id, organization.Id, activityId);
        }

        private static bool TryApply(Organization organization, EnrichmentSuggestion suggestion)
        {
            var value = suggestion.ProposedValue;
            switch (suggestion.Field)
            {
                case EnrichmentField.Address:
                    if (EnrichmentAddress.TryParse(value) is not { } address)
                    {
                        return false;
                    }

                    Update(organization, address: address.ToAddress());
                    return true;

                case EnrichmentField.Phone when value.Length <= Organization.PhoneMaxLength:
                    Update(organization, phone: value);
                    return true;

                case EnrichmentField.Email when value.Length <= Organization.EmailMaxLength && value.Contains('@', StringComparison.Ordinal):
                    Update(organization, email: value);
                    return true;

                default:
                    if (EnrichmentFieldKeys.ExtraFieldKey(suggestion.Field) is not { } key)
                    {
                        return false;
                    }

                    organization.SetExtraField(key, value);
                    return true;
            }
        }

        /// <summary>Uses the regular update with all other values unchanged.</summary>
        private static void Update(Organization organization, Address? address = null, string? phone = null, string? email = null) =>
            organization.Update(
                organization.Name,
                organization.Type,
                organization.Website,
                address ?? organization.Address,
                organization.Notes,
                phone ?? organization.Phone,
                email ?? organization.Email);

        private static string ProfileBody(EnrichmentSuggestion suggestion) =>
            $"{suggestion.ProposedValue}\n\n**Quellen:**\n{string.Join('\n', suggestion.SourceUrls.Select(u => $"- {u}"))}";
    }
}
