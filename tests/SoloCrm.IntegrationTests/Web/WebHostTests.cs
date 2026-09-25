namespace SoloCrm.IntegrationTests.Web;

/// <summary>
/// Web host tests must not start hosts in parallel: Program.cs uses Serilog's static
/// bootstrap logger, which can be frozen only once at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WebHostTests
{
    public const string Name = "WebHost";
}
