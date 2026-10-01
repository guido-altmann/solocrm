using System.Globalization;

namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// German date formats for the UI. Times are expected in the configured time zone (<c>AppClock.ToLocal</c>).
/// </summary>
public static class Formats
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", German);

    /// <summary>Whole number with thousands separator, e.g. „10.000“.</summary>
    public static string Number(int value) => value.ToString("N0", German);

    public static string Date(DateTimeOffset localTime) => localTime.ToString("dd.MM.yyyy", German);

    public static string DateTime(DateTimeOffset localTime) => localTime.ToString("dd.MM.yyyy, HH:mm", German);

    /// <summary>„Dienstag, 29. September 2026“.</summary>
    public static string LongDate(DateOnly date) => date.ToString("dddd, d. MMMM yyyy", German);

    /// <summary>„heute“, „seit 1 Tag“, „seit 5 Tagen“ (dative after „seit“).</summary>
    public static string SinceDays(int days) => days switch
    {
        <= 0 => "heute",
        1 => "seit 1 Tag",
        _ => $"seit {days.ToString(German)} Tagen",
    };

    /// <summary>„heute“, „morgen“, „gestern“ or the date.</summary>
    public static string DueDate(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "heute",
        1 => "morgen",
        -1 => "gestern",
        _ => Date(date),
    };
}
