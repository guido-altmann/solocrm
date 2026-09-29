using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Opportunities;

/// <summary>
/// Records a project request; only the title is required, the stage defaults to the first open one (US-06).
/// </summary>
public static class CreateOpportunity
{
    public sealed record Command(
        string? Title,
        Guid? StageId = null,
        Guid? ClientOrganizationId = null,
        Guid? AgencyOrganizationId = null,
        Guid? PrimaryContactId = null,
        PricingModel? PricingModel = null,
        decimal? Amount = null,
        string? Currency = null,
        DateOnly? StartDate = null,
        int? DurationValue = null,
        DurationUnit? DurationUnit = null,
        int? Utilization = null,
        int? RemotePercentage = null,
        LeadSource? Source = null) : IOpportunityFields;

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            Include(new OpportunityFieldsValidator<Command>());
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IAppSettings settings, IValidator<Command> validator)
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

            var stage = command.StageId is { } stageId
                ? await db.Stages.SingleOrDefaultAsync(s => s.Id == stageId, cancellationToken)
                : await db.Stages
                    .Where(s => s.Status == StageStatus.Open)
                    .OrderBy(s => s.SortOrder)
                    .FirstOrDefaultAsync(cancellationToken);

            if (stage is null)
            {
                return command.StageId is null ? OpportunityErrors.NoOpenStage : OpportunityErrors.StageNotFound;
            }

            if (stage.Status != StageStatus.Open)
            {
                return OpportunityErrors.StageNotOpen;
            }

            if (await OpportunityRules.CheckReferencesAsync(db, command, cancellationToken) is { } referenceError)
            {
                return referenceError;
            }

            var details = await OpportunityRules.ToDetailsAsync(command, settings, cancellationToken);
            var opportunity = Opportunity.Create(command.Title!, stage, details);

            db.Opportunities.Add(opportunity);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(opportunity.Id);
        }
    }
}
