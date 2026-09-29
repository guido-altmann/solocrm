using System.Globalization;

namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// German date formats for the UI. Times are expected in the configured time zone (<c>AppClock.ToLocal</c>).
/// </summary>
public static class Formats
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", German);

    public static string Date(DateTimeOffset localTime) => localTime.ToString("dd.MM.yyyy", German);

    public static string DateTime(DateTimeOffset localTime) => localTime.ToString("dd.MM.yyyy, HH:mm", German);

    /// <summary>„heute“, „morgen“, „gestern“ or the date.</summary>
    public static string DueDate(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "heute",
        1 => "morgen",
        -1 => "gestern",
        _ => Date(date),
    };

    /// <summary>„1 Tag“, „5 Tage“.</summary>
    public static string Days(int days) => days == 1 ? "1 Tag" : $"{days.ToString(German)} Tage";
}
