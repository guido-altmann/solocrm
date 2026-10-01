using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>
/// Finds an organization by its exact name regardless of case and surrounding whitespace, so that the REST API and
/// the CSV import reuse an existing organization instead of creating a duplicate (iteration 5 decision 9).
/// Active organizations win over archived ones; among equals the oldest wins.
/// </summary>
public static class FindOrganizationByName
{
    public sealed record Query(string? Name);

    public sealed record Result(Guid? Id);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Name)
                .NotEmpty()
                .WithMessage("Bitte einen Namen angeben.")
                .MaximumLength(Organization.NameMaxLength)
                .WithMessage($"Der Name darf höchstens {Organization.NameMaxLength} Zeichen lang sein.");
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
            var id = await OrganizationLookup.FindByNameAsync(db, query.Name!, cancellationToken);
            return new Result(id);
        }
    }
}

internal static class OrganizationLookup
{
    /// <summary>
    /// Case-insensitive via <c>ILIKE</c> without wildcards (escaped); organization names are plain varchar, unlike
    /// tag names (citext).
    /// </summary>
    public static Task<Guid?> FindByNameAsync(ICrmDbContext db, string name, CancellationToken cancellationToken)
    {
        var pattern = name.Trim().Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
        return db.Organizations.AsNoTracking()
            .Where(o => EF.Functions.ILike(o.Name, pattern, @"\"))
            .OrderBy(o => o.IsArchived)
            .ThenBy(o => o.CreatedAt)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
