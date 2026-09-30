using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Timeline;

namespace SoloCrm.Application.Features.Tags;

/// <summary>Tags of a contact, organization or request, ordered by name (detail views, US-15).</summary>
public static class GetRecordTags
{
    public sealed record Query(TimelineRecordType RecordType, Guid RecordId);

    public sealed record Result(IReadOnlyList<TagRef> Items);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.RecordType).IsInEnum().WithMessage("Unbekannter Datensatztyp.");
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
            var items = await TagRules.TagsOf(db, query.RecordType, query.RecordId).ToListAsync(cancellationToken);
            return new Result(items);
        }
    }
}
