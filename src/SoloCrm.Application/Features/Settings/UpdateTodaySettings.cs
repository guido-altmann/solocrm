using FluentValidation;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Settings;

/// <summary>Saves the threshold for „eingeschlafene“ requests on „Heute“ (US-12 AK2).</summary>
public static class UpdateTodaySettings
{
    public const int MaxStaleOpportunityDays = 365;

    public sealed record Command(int StaleOpportunityDays);

    public sealed record Result;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.StaleOpportunityDays)
                .InclusiveBetween(1, MaxStaleOpportunityDays)
                .WithMessage($"Der Schwellwert muss zwischen 1 und {MaxStaleOpportunityDays} Tagen liegen.");
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

            await settings.SetAsync(AppSettingKeys.StaleOpportunityDays, command.StaleOpportunityDays, cancellationToken);
            return new Result();
        }
    }
}
