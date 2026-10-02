using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Opportunities;

/// <summary>Editable fields shared by <see cref="CreateOpportunity"/> and <see cref="UpdateOpportunity"/>.</summary>
public interface IOpportunityFields
{
    string? Title { get; }

    Guid? ClientOrganizationId { get; }

    Guid? AgencyOrganizationId { get; }

    Guid? PrimaryContactId { get; }

    /// <summary>Without an <see cref="Amount"/> the opportunity has no pricing. Defaults to the <c>DefaultPricingModel</c> setting.</summary>
    PricingModel? PricingModel { get; }

    decimal? Amount { get; }

    /// <summary>ISO 4217 code; defaults to the <c>DefaultCurrency</c> setting.</summary>
    string? Currency { get; }

    DateOnly? StartDate { get; }

    /// <summary>Without a value the duration is open-ended.</summary>
    int? DurationValue { get; }

    DurationUnit? DurationUnit { get; }

    int? Utilization { get; }

    int? RemotePercentage { get; }

    LeadSource? Source { get; }

    /// <summary>
    /// When the request came in (iteration 6 decision 11); not in the future. <c>null</c> means today on creation and
    /// unchanged on update.
    /// </summary>
    DateOnly? ReceivedOn { get; }
}

public sealed class OpportunityFieldsValidator<T> : AbstractValidator<T>
    where T : IOpportunityFields
{
    public OpportunityFieldsValidator(AppClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        RuleFor(c => c.ReceivedOn)
            .Must(d => d <= clock.Today)
            .WithMessage("Das Eingangsdatum darf nicht in der Zukunft liegen.")
            .When(c => c.ReceivedOn is not null);

        RuleFor(c => c.Title)
            .NotEmpty()
            .WithMessage("Bitte einen Titel angeben.")
            .MaximumLength(Opportunity.TitleMaxLength)
            .WithMessage($"Der Titel darf höchstens {Opportunity.TitleMaxLength} Zeichen lang sein.");

        RuleFor(c => c.PricingModel)
            .IsInEnum()
            .WithMessage("Bitte ein gültiges Preismodell wählen.");

        RuleFor(c => c.Amount)
            .GreaterThan(0)
            .WithMessage("Der Betrag muss größer als 0 sein.")
            .LessThanOrEqualTo(Pricing.MaxAmount)
            .WithMessage("Der Betrag ist zu groß.")
            .PrecisionScale(12, 2, ignoreTrailingZeros: true)
            .WithMessage("Der Betrag darf höchstens zwei Nachkommastellen haben.");

        RuleFor(c => c.Currency)
            .Must(Currency.IsValid)
            .WithMessage("Bitte einen gültigen Währungscode (ISO 4217, z. B. EUR) angeben.")
            .When(c => c.Currency is not null);

        RuleFor(c => c.DurationValue)
            .InclusiveBetween(1, Duration.MaxValue)
            .WithMessage($"Die Laufzeit muss zwischen 1 und {Duration.MaxValue} liegen.");

        RuleFor(c => c.DurationUnit)
            .NotNull()
            .WithMessage("Bitte eine Einheit für die Laufzeit wählen.")
            .When(c => c.DurationValue is not null);

        RuleFor(c => c.DurationUnit)
            .IsInEnum()
            .WithMessage("Bitte eine gültige Einheit wählen.");

        RuleFor(c => c.Utilization)
            .InclusiveBetween(0, 100)
            .WithMessage("Die Auslastung muss zwischen 0 und 100 % liegen.");

        RuleFor(c => c.RemotePercentage)
            .InclusiveBetween(0, 100)
            .WithMessage("Der Remote-Anteil muss zwischen 0 und 100 % liegen.");

        RuleFor(c => c.Source)
            .IsInEnum()
            .WithMessage("Bitte eine gültige Quelle wählen.");
    }
}

public static class OpportunityErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Opportunity.NotFound", "Die Anfrage wurde nicht gefunden.");

    public static Error StageNotFound { get; } =
        Error.NotFound("Opportunity.StageNotFound", "Die gewählte Phase existiert nicht.");

    public static Error StageNotOpen { get; } =
        Error.Conflict("Opportunity.StageNotOpen", "Neue Anfragen starten in einer offenen Phase.");

    public static Error NoOpenStage { get; } =
        Error.Conflict("Opportunity.NoOpenStage", "Es gibt keine offene Phase für neue Anfragen.");

    public static Error LostReasonRequired { get; } =
        Error.Conflict("Opportunity.LostReasonRequired", "Bitte einen Absagegrund angeben.");

    public static Error ClientNotFound { get; } =
        Error.NotFound("Opportunity.ClientNotFound", "Der gewählte Endkunde existiert nicht.");

    public static Error AgencyNotFound { get; } =
        Error.NotFound("Opportunity.AgencyNotFound", "Der gewählte Vermittler existiert nicht.");

    public static Error ContactNotFound { get; } =
        Error.NotFound("Opportunity.ContactNotFound", "Der gewählte Ansprechpartner existiert nicht.");

    /// <summary>The form field a business error belongs to, or <c>null</c>.</summary>
    public static string? FieldOf(Error error) =>
        error == StageNotFound || error == StageNotOpen ? "StageId"
        : error == LostReasonRequired ? "LostReason"
        : error == ClientNotFound ? nameof(IOpportunityFields.ClientOrganizationId)
        : error == AgencyNotFound ? nameof(IOpportunityFields.AgencyOrganizationId)
        : error == ContactNotFound ? nameof(IOpportunityFields.PrimaryContactId)
        : null;
}

internal static class OpportunityRules
{
    /// <summary>Checks that referenced organizations and the contact exist.</summary>
    public static async Task<Error?> CheckReferencesAsync(ICrmDbContext db, IOpportunityFields fields, CancellationToken cancellationToken)
    {
        if (fields.ClientOrganizationId is { } clientId && !await db.Organizations.AnyAsync(o => o.Id == clientId, cancellationToken))
        {
            return OpportunityErrors.ClientNotFound;
        }

        if (fields.AgencyOrganizationId is { } agencyId && !await db.Organizations.AnyAsync(o => o.Id == agencyId, cancellationToken))
        {
            return OpportunityErrors.AgencyNotFound;
        }

        if (fields.PrimaryContactId is { } contactId && !await db.Contacts.AnyAsync(c => c.Id == contactId, cancellationToken))
        {
            return OpportunityErrors.ContactNotFound;
        }

        return null;
    }

    /// <summary>Builds the domain details; missing model and currency fall back to the settings.</summary>
    public static async Task<OpportunityDetails> ToDetailsAsync(IOpportunityFields fields, IAppSettings settings, CancellationToken cancellationToken)
    {
        Pricing? pricing = null;
        if (fields.Amount is { } amount)
        {
            var model = fields.PricingModel ?? await settings.GetAsync(AppSettingKeys.DefaultPricingModel, cancellationToken);
            var currency = fields.Currency ?? await settings.GetAsync(AppSettingKeys.DefaultCurrency, cancellationToken);
            pricing = Pricing.Create(model, amount, currency);
        }

        var duration = fields.DurationValue is { } value ? Duration.Create(value, fields.DurationUnit!.Value) : null;

        return new OpportunityDetails(
            fields.ClientOrganizationId,
            fields.AgencyOrganizationId,
            fields.PrimaryContactId,
            pricing,
            fields.StartDate,
            duration,
            fields.Utilization,
            fields.RemotePercentage,
            fields.Source);
    }
}
