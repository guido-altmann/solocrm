using System.Globalization;

namespace SoloCrm.Domain.Opportunities;

/// <summary>
/// ISO 4217 currency codes and German money formatting. Deliberately independent of ICU/culture data,
/// so validation and display are identical in every environment.
/// </summary>
public static class Currency
{
    public const string Euro = "EUR";

    // Active ISO 4217 codes (without funds, precious metals and test codes).
    private static readonly HashSet<string> Codes = new(
        """
        AED AFN ALL AMD ANG AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD BIF BMD BND BOB BRL BSD BTN BWP BYN BZD
        CAD CDF CHF CLP CNY COP CRC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS GIP GMD
        GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR KMF KPW KRW KWD KYD KZT
        LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN MYR MZN NAD NGN NIO NOK NPR
        NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR SDG SEK SGD SHP SLE SOS SRD SSP
        STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD UYU UZS VES VND VUV WST XAF XCD XCG
        XOF XPF YER ZAR ZMW ZWG
        """.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
        StringComparer.Ordinal);

    private static readonly NumberFormatInfo German = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NegativeSign = "-",
    };

    /// <summary>Upper-case three-letter ISO 4217 code, e.g. <c>EUR</c>.</summary>
    public static bool IsValid(string? code) => code is not null && Codes.Contains(code);

    public static string Symbol(string code) => code switch
    {
        "EUR" => "€",
        "USD" => "$",
        "GBP" => "£",
        _ => code,
    };

    /// <summary>German notation, cents only when present: <c>2.500 €</c>, <c>95,50 €</c>.</summary>
    public static string Format(decimal amount, string code)
    {
        var number = amount == decimal.Truncate(amount)
            ? amount.ToString("#,0", German)
            : amount.ToString("#,0.00", German);
        return $"{number} {Symbol(code)}";
    }
}
