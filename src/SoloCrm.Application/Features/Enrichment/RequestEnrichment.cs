using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Application.Features.Enrichment;

/// <summary>
/// Queues the enrichment of an organization (ADR-016). The <c>EnrichmentProcessor</c> picks the request up and stores
/// the suggestions. A website is not required: the agent also searches by name.
/// </summary>
public static class RequestEnrichment
{
    public static readonly Error NotConfigured =
        Error.Failure("Enrichment.NotConfigured", "Die Anreicherung ist nicht eingerichtet.");

    public static readonly Error AlreadyActive =
        Error.Conflict("Enrichment.AlreadyActive", "Für diese Organisation läuft bereits eine Anreicherung.");

    public static readonly Error Archived =
        Error.Conflict("Enrichment.Archived", "Eine archivierte Organisation wird nicht angereichert.");

    public static Error DailyLimitReached(int limit) =>
        Error.Failure("Enrichment.DailyLimit", $"Das Tageslimit von {limit} Anreicherungen ist erreicht. Morgen geht es weiter.");

    public sealed record Command(Guid OrganizationId);

    public sealed record Result(Guid RequestId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.OrganizationId).NotEmpty().WithMessage("Die Organisation fehlt.");
        }
    }

    public sealed class Handler(
        ICrmDbContextFactory dbFactory,
        IValidator<Command> validator,
        IEnrichmentAvailability availability,
        IEnrichmentProvider provider,
        EnrichmentOptions options,
        AppClock clock) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            if (!availability.IsAvailable)
            {
                return NotConfigured;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var organization = await db.Organizations
                .Where(o => o.Id == command.OrganizationId)
                .Select(o => new { o.IsArchived })
                .SingleOrDefaultAsync(cancellationToken);
            if (organization is null)
            {
                return OrganizationErrors.NotFound;
            }

            if (organization.IsArchived)
            {
                return Archived;
            }

            if (await HasActiveRequestAsync(db, command.OrganizationId, cancellationToken))
            {
                return AlreadyActive;
            }

            var since = clock.StartOfDayUtc(clock.Today);
            if (await db.EnrichmentRequests.CountAsync(r => r.CreatedAt >= since, cancellationToken) >= options.MaxRequestsPerDay)
            {
                return DailyLimitReached(options.MaxRequestsPerDay);
            }

            var request = EnrichmentRequest.Create(EnrichmentTargetType.Organization, command.OrganizationId, provider.Name, clock.UtcNow);
            db.EnrichmentRequests.Add(request);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A second click was faster: the unique index allows one active request per organization.
                await using var verify = await dbFactory.CreateDbContextAsync(cancellationToken);
                if (await HasActiveRequestAsync(verify, command.OrganizationId, cancellationToken))
                {
                    return AlreadyActive;
                }

                throw;
            }

            return new Result(request.Id);
        }

        private static Task<bool> HasActiveRequestAsync(ICrmDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
            db.EnrichmentRequests.AnyAsync(
                r => r.TargetType == EnrichmentTargetType.Organization
                    && r.TargetId == organizationId
                    && (r.Status == EnrichmentRequestStatus.Pending || r.Status == EnrichmentRequestStatus.Running),
                cancellationToken);
    }
}
