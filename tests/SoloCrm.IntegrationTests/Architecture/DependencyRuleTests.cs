using System.Reflection;
using NetArchTest.Rules;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Common;
using SoloCrm.Infrastructure.Persistence;

namespace SoloCrm.IntegrationTests.Architecture;

/// <summary>
/// Enforces the layering rule from CLAUDE.md / SPEC 7: Domain → nothing, Application → Domain only,
/// Infrastructure → Application, Web is the composition root.
/// </summary>
public sealed class DependencyRuleTests
{
    private const string Domain = "SoloCrm.Domain";
    private const string Application = "SoloCrm.Application";
    private const string Infrastructure = "SoloCrm.Infrastructure";
    private const string Web = "SoloCrm.Web";

    private static readonly Assembly DomainAssembly = typeof(Entity).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ICrmDbContext).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(CrmDbContext).Assembly;

    [Fact]
    public void Domain_Always_DependsOnNoOtherLayerOrFramework()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, Web, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Domain_Always_ReferencesOnlyFrameworkAssemblies()
    {
        var references = DomainAssembly.GetReferencedAssemblies().Select(a => a.Name);

        references.Should().OnlyContain(name => name == "System.Runtime" || name!.StartsWith("System.", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_Always_DependsOnlyOnDomain()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Web, "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_Always_DoesNotDependOnWeb()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(Web)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    /// <summary>Rule 4: Blazor components use handlers, never the DbContext.</summary>
    [Fact]
    public void WebComponents_Always_DoNotUseDbContext()
    {
        var result = Types.InAssembly(typeof(SoloCrm.Web.Components.Shared.QuickAddService).Assembly)
            .That()
            .ResideInNamespace("SoloCrm.Web.Components")
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "SoloCrm.Infrastructure.Persistence", typeof(ICrmDbContext).FullName)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        $"these types violate the dependency rule: {string.Join(", ", result.FailingTypeNames ?? [])}";
}
