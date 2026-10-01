using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>
/// Creates an organization; only the name is required (US-03).
/// </summary>
public static class CreateOrganization
{
    public sealed record Command(
        string? Name,
        OrganizationType Type = OrganizationType.Other,
        string? Website = null,
        AddressData? Address = null,
        string? Notes = null) : IOrganizationFields;

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

            var organization = Organization.Create(command.Name!, command.Type, command.Website, command.Address?.ToAddress(), command.Notes);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.Organizations.Add(organization);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(organization.Id);
        }
    }
}
