namespace SoloCrm.Domain.Opportunities;

/// <summary>User settings the valuation depends on (SPEC 2.3, defaults 8 h/day and 12 months).</summary>
public sealed record ValuationSettings(decimal HoursPerDay, int RetainerValuationMonths)
{
    public static ValuationSettings Default { get; } = new(8m, 12);
}
