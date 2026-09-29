using SoloCrm.Domain.Organizations;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class OrganizationOptionTests
{
    [Fact]
    public void ToCommand_ExistingOrganization_ReturnsIdOnly()
    {
        var id = Guid.CreateVersion7();

        OrganizationOption.ToCommand(new OrganizationOption(id, "Contoso", OrganizationType.Client)).Should().Be((id, (string?)null));
    }

    [Fact]
    public void ToCommand_NewOrganization_ReturnsNameOnly()
    {
        OrganizationOption.ToCommand(new OrganizationOption(null, "Neu GmbH", null)).Should().Be(((Guid?)null, "Neu GmbH"));
    }

    [Fact]
    public void ToCommand_Nothing_ReturnsNeither()
    {
        OrganizationOption.ToCommand(null).Should().Be(((Guid?)null, (string?)null));
    }
}
