using SoloCrm.Domain.Organizations;

namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// Selection of <see cref="OrganizationAutocomplete"/>: an existing organization, or (without <see cref="Id"/>)
/// one that is created together with the edited object.
/// </summary>
public sealed record OrganizationOption(Guid? Id, string Name, OrganizationType? Type)
{
    public bool IsNew => Id is null;

    /// <summary>Splits the option into the command fields <c>OrganizationId</c> / <c>NewOrganizationName</c>.</summary>
    public static (Guid? Id, string? NewName) ToCommand(OrganizationOption? option) =>
        option is null ? (null, null) : (option.Id, option.IsNew ? option.Name : null);

    public static OrganizationOption? Existing(Guid? id, string? name, OrganizationType? type) =>
        id is { } value && name is not null ? new OrganizationOption(value, name, type) : null;
}
