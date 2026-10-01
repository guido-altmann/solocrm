namespace SoloCrm.IntegrationTests.Performance;

/// <summary>
/// Time budgets are measured without the other test classes running: each of them starts its own Postgres container,
/// and their load would be measured instead of the query. xUnit runs this collection after the parallel ones.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceRuns
{
    public const string Name = "Performance";
}
