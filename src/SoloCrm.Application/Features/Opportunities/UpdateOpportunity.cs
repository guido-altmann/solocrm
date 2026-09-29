using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Opportunities;

/// <summary>
/// Replaces the fields of a request; a different stage is applied via <see cref="Opportunity.ChangeStage"/> (US-06, US-08).
/// </summary>
public static class UpdateOpportunity
{
    public sealed record Command(
        Guid Id,
        string? Title,
        Guid StageId,
        LostReason? LostReason,
        Guid? ClientOrganizationId,
        Guid? AgencyOrganizationId,
        Guid? PrimaryContactId,
        PricingModel? PricingModel,
        decimal? Amount,
        string? Currency,
        DateOnly? StartDate,
        int? DurationValue,
        DurationUnit? DurationUnit,
        int? Utilization,
        int? RemotePercentage,
        LeadSource? Source) : IOpportunityFields;

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            Include(new OpportunityFieldsValidator<Command>());
            RuleFor(c => c.LostReason).IsInEnum().WithMessage("Bitte einen gültigen Absagegrund wählen.");
        }
    }

    public sealed class Handler(
        ICrmDbContextFactory dbFactory,
        IAppSettings settings,
        TimeProvider timeProvider,
        IValidator<Command> validator)
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
            var opportunity = await db.Opportunities.SingleOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
            if (opportunity is null)
            {
                return OpportunityErrors.NotFound;
            }

            var stage = await db.Stages.SingleOrDefaultAsync(s => s.Id == command.StageId, cancellationToken);
            if (stage is null)
            {
                return OpportunityErrors.StageNotFound;
            }

            if (command.LostReason is null && opportunity.RequiresLostReason(stage))
            {
                return OpportunityErrors.LostReasonRequired;
            }

            if (await OpportunityRules.CheckReferencesAsync(db, command, cancellationToken) is { } referenceError)
            {
                return referenceError;
            }

            var details = await OpportunityRules.ToDetailsAsync(command, settings, cancellationToken);
            opportunity.Update(command.Title!, details);
            opportunity.ChangeStage(stage, command.LostReason, timeProvider.GetUtcNow());

            await db.SaveChangesAsync(cancellationToken);

            return new Result(opportunity.Id);
        }
    }
}
