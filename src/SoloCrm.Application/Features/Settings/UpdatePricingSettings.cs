using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Settings;

/// <summary>
/// Saves the pricing defaults and the valuation parameters of <c>EstimatedValue</c> (SPEC 2.3 „Einstellungen“, S6).
/// </summary>
public static class UpdatePricingSettings
{
    public const decimal MaxHoursPerDay = 24m;
    public const int MaxRetainerValuationMonths = 120;

    public sealed record Command(
        PricingModel DefaultPricingModel,
        string? DefaultCurrency,
        decimal HoursPerDay,
        int RetainerValuationMonths);

    public sealed record Result;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.DefaultPricingModel)
                .IsInEnum()
                .WithMessage("Bitte ein gültiges Preismodell wählen.");

            RuleFor(c => c.DefaultCurrency)
                .Must(code => Currency.IsValid(code?.Trim().ToUpperInvariant()))
                .WithMessage("Bitte einen gültigen Währungscode (ISO 4217, z. B. EUR) angeben.");

            RuleFor(c => c.HoursPerDay)
                .GreaterThan(0)
                .WithMessage("Die Stunden pro Tag müssen größer als 0 sein.")
                .LessThanOrEqualTo(MaxHoursPerDay)
                .WithMessage($"Ein Tag hat höchstens {MaxHoursPerDay:0} Stunden.")
                .PrecisionScale(4, 2, ignoreTrailingZeros: true)
                .WithMessage("Bitte höchstens zwei Nachkommastellen angeben.");

            RuleFor(c => c.RetainerValuationMonths)
                .InclusiveBetween(1, MaxRetainerValuationMonths)
                .WithMessage($"Der Bewertungszeitraum muss zwischen 1 und {MaxRetainerValuationMonths} Monaten liegen.");
        }
    }

    public sealed class Handler(IAppSettings settings, IValidator<Command> validator) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await settings.SetAsync(AppSettingKeys.DefaultPricingModel, command.DefaultPricingModel, cancellationToken);
            await settings.SetAsync(AppSettingKeys.DefaultCurrency, command.DefaultCurrency!.Trim().ToUpperInvariant(), cancellationToken);
            await settings.SetAsync(AppSettingKeys.HoursPerDay, command.HoursPerDay, cancellationToken);
            await settings.SetAsync(AppSettingKeys.RetainerValuationMonths, command.RetainerValuationMonths, cancellationToken);

            return new Result();
        }
    }
}
