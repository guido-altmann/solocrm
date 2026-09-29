namespace SoloCrm.Domain.Opportunities;

/// <summary>
/// Planned duration of an opportunity (SPEC 2.3, ADR-011). <c>null</c> on the opportunity means open-ended.
/// </summary>
public sealed record Duration
{
    public const int WorkingDaysPerWeek = 5;
    public const int WorkingDaysPerMonth = 20;
    public const int MaxValue = 999;

    private Duration(int value, DurationUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public int Value { get; private init; }

    public DurationUnit Unit { get; private init; }

    /// <exception cref="ArgumentOutOfRangeException">Value not in 1..<see cref="MaxValue"/> or unknown unit.</exception>
    public static Duration Create(int value, DurationUnit unit)
    {
        if (value is <= 0 or > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"The duration must be between 1 and {MaxValue}.");
        }

        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown duration unit.");
        }

        return new Duration(value, unit);
    }

    /// <summary>Days as given, weeks × 5, months × 20.</summary>
    public int ToWorkingDays() => Unit switch
    {
        DurationUnit.Days => Value,
        DurationUnit.Weeks => Value * WorkingDaysPerWeek,
        _ => Value * WorkingDaysPerMonth,
    };

    /// <summary>Months as given; days and weeks via working days / 20, rounded up to whole months.</summary>
    public int ToMonths() => Unit == DurationUnit.Months
        ? Value
        : (ToWorkingDays() + WorkingDaysPerMonth - 1) / WorkingDaysPerMonth;
}
