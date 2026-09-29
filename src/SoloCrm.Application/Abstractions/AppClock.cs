namespace SoloCrm.Application.Abstractions;

/// <summary>
/// Current time in the configured time zone (<c>App:TimeZone</c>, default <c>Europe/Berlin</c>). "Today",
/// "overdue" and the times shown in the UI refer to this zone; the database stores UTC (SPEC 3.3 S1).
/// </summary>
public sealed class AppClock(TimeProvider timeProvider, TimeZoneInfo timeZone)
{
    public const string DefaultTimeZoneId = "Europe/Berlin";

    public TimeZoneInfo TimeZone { get; } = timeZone;

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    /// <summary>Now, with the offset of the configured time zone.</summary>
    public DateTimeOffset Now => ToLocal(UtcNow);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZone);

    /// <summary>Interprets a wall-clock time (e.g. from a date/time picker) in the configured time zone.</summary>
    public DateTimeOffset FromLocal(DateTime localDateTime)
    {
        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZone.GetUtcOffset(unspecified));
    }

    /// <summary>Calendar days from the local date of <paramref name="value"/> until today (0 = today).</summary>
    public int DaysSince(DateTimeOffset value) => Today.DayNumber - DateOnly.FromDateTime(ToLocal(value).DateTime).DayNumber;

    /// <summary>Start of <paramref name="date"/> in the configured time zone, as UTC.</summary>
    public DateTimeOffset StartOfDayUtc(DateOnly date) =>
        FromLocal(date.ToDateTime(TimeOnly.MinValue)).ToUniversalTime();
}
