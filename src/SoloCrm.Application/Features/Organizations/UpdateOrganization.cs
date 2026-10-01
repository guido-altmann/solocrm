using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>
/// Replaces the master data of an organization (US-03).
/// </summary>
public static class UpdateOrganization
{
    public sealed record Command(
        Guid Id,
        string? Name,
        OrganizationType Type,
        string? Website,
        AddressData? Address,
        string? Notes) : IOrganizationFields;

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            Include(new OrganizationFieldsValidator<Command>());
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator)
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
            var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
            if (organization is null)
            {
                return OrganizationErrors.NotFound;
            }

            organization.Update(command.Name!, command.Type, command.Website, command.Address?.ToAddress(), command.Notes);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(organization.Id);
        }
    }
}
