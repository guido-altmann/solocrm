namespace SoloCrm.Domain.Opportunities;

/// <summary>Meaning of <c>Pricing.Amount</c> (SPEC 2.3).</summary>
public enum PricingModel
{
    /// <summary>Hourly rate (default).</summary>
    Hourly,

    /// <summary>Daily rate.</summary>
    Daily,

    /// <summary>Total price.</summary>
    FixedPrice,

    /// <summary>Monthly amount.</summary>
    Retainer,
}
