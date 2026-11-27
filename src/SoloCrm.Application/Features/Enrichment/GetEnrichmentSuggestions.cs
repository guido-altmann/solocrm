using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Application.Features.Enrichment;

/// <summary>
/// The open suggestions of an organization with their live value, and the state of the latest enrichment request
/// (SPEC 9.3). Superseded, accepted and rejected suggestions are not listed.
/// </summary>
public static class GetEnrichmentSuggestions
{
    public sealed record Query(Guid OrganizationId);

    /// <param name="Id">The suggestion.</param>
    /// <param name="ProposedText">Readable proposal (address as one line).</param>
    /// <param name="LiveText">What the organization has today, <c>null</c> if empty; may differ from the value at suggestion time.</param>
    /// <param name="IsUnchanged">The proposal equals the live value, so accepting would change nothing.</param>
    public sealed record SuggestionItem(
        Guid Id,
        EnrichmentField Field,
        string Label,
        string ProposedText,
        string? LiveText,
        IReadOnlyList<string> SourceUrls,
        decimal Confidence,
        bool IsUnchanged);

    /// <param name="Error">Only for <see cref="EnrichmentRequestStatus.Failed"/>: the stored short message.</param>
    public sealed record RequestState(Guid Id, EnrichmentRequestStatus Status, DateTimeOffset RequestedAt, string? Error);

    /// <param name="LatestRequest"><c>null</c> if the organization was never enriched.</param>
    public sealed record Result(RequestState? LatestRequest, IReadOnlyList<SuggestionItem> Suggestions);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.OrganizationId).NotEmpty().WithMessage("Die Organisation fehlt.");
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Query> validator) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(query, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == query.OrganizationId, cancellationToken);
            if (organization is null)
            {
                return OrganizationErrors.NotFound;
            }

            var latest = await db.EnrichmentRequests.AsNoTracking()
                .Where(r => r.TargetType == EnrichmentTargetType.Organization && r.TargetId == query.OrganizationId)
                .OrderByDescending(r => r.RequestedAt)
                .ThenByDescending(r => r.Id)
                .Select(r => new RequestState(r.Id, r.Status, r.RequestedAt, r.Error))
                .FirstOrDefaultAsync(cancellationToken);

            var open = await db.EnrichmentSuggestions.AsNoTracking()
                .Where(s => s.Status == EnrichmentSuggestionStatus.Open
                    && db.EnrichmentRequests.Any(r => r.Id == s.RequestId
                        && r.TargetType == EnrichmentTargetType.Organization
                        && r.TargetId == query.OrganizationId))
                .OrderBy(s => s.Field)
                .ThenBy(s => s.Id)
                .ToListAsync(cancellationToken);

            var items = open.Select(s =>
            {
                var live = EnrichmentValues.LiveValue(organization, s.Field);
                return new SuggestionItem(
                    s.Id,
                    s.Field,
                    EnrichmentValues.Label(s.Field),
                    EnrichmentValues.Display(s.Field, s.ProposedValue),
                    live is null ? null : EnrichmentValues.Display(s.Field, live),
                    s.SourceUrls,
                    s.Confidence,
                    live is not null && string.Equals(live, s.ProposedValue, StringComparison.Ordinal));
            }).ToList();

            return new Result(latest, items);
        }
    }
}
