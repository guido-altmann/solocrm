namespace SoloCrm.Domain.Opportunities;

/// <summary>
/// Pricing model and amount of an opportunity (SPEC 2.3, ADR-011). Immutable; mapped as EF Core complex type.
/// </summary>
public sealed record Pricing
{
    /// <summary>Upper bound of <c>decimal(12,2)</c>.</summary>
    public const decimal MaxAmount = 9_999_999_999.99m;

    private Pricing(PricingModel model, decimal amount, string currency)
    {
        Model = model;
        Amount = amount;
        Currency = currency;
    }

    public PricingModel Model { get; private init; }

    /// <summary>Hourly/daily rate, total price or monthly amount, depending on <see cref="Model"/>.</summary>
    public decimal Amount { get; private init; }

    /// <summary>ISO 4217 code.</summary>
    public string Currency { get; private init; }

    /// <exception cref="ArgumentException">Unknown model, amount not in (0, <see cref="MaxAmount"/>] or more than two decimals, unknown currency.</exception>
    public static Pricing Create(PricingModel model, decimal amount, string currency = Opportunities.Currency.Euro)
    {
        if (!Enum.IsDefined(model))
        {
            throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown pricing model.");
        }

        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount must be positive with at most two decimals.");
        }

        if (!Opportunities.Currency.IsValid(currency))
        {
            throw new ArgumentException("Unknown ISO 4217 currency code.", nameof(currency));
        }

        return new Pricing(model, amount, currency);
    }

    /// <summary>Display in the format of the model: „95 €/h“, „760 €/Tag“, „8.000 € fix“, „2.500 €/Monat“.</summary>
    public string ToDisplayString()
    {
        var amount = Opportunities.Currency.Format(Amount, Currency);
        return Model switch
        {
            PricingModel.Hourly => $"{amount}/h",
            PricingModel.Daily => $"{amount}/Tag",
            PricingModel.FixedPrice => $"{amount} fix",
            _ => $"{amount}/Monat",
        };
    }
}
