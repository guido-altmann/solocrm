using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Enrichment;

/// <summary>Rejects one suggestion; the organization stays unchanged.</summary>
public static class RejectSuggestion
{
    public sealed record Command(Guid SuggestionId);

    public sealed record Result(Guid SuggestionId);

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

            suggestion.Reject(clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(suggestion.Id);
        }
    }
}
