using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// German display names for domain enums (the UI is German, identifiers are English).
/// </summary>
public static class Labels
{
    public static string For(OrganizationType type) => type switch
    {
        OrganizationType.Client => "Endkunde",
        OrganizationType.Agency => "Vermittler",
        OrganizationType.Partner => "Partner",
        _ => "Sonstige",
    };

    public static string For(LeadSource source) => source switch
    {
        LeadSource.LinkedIn => "LinkedIn",
        LeadSource.ProjectPortal => "Projektportal",
        LeadSource.Referral => "Empfehlung",
        LeadSource.Website => "Website",
        LeadSource.Event => "Event",
        _ => "Sonstige",
    };

    public static string For(PricingModel model) => model switch
    {
        PricingModel.Hourly => "Stundensatz",
        PricingModel.Daily => "Tagessatz",
        PricingModel.FixedPrice => "Festpreis",
        _ => "Retainer",
    };

    /// <summary>Label of the amount field, depending on the pricing model (US-06 AK3).</summary>
    public static string AmountLabel(PricingModel model) => model switch
    {
        PricingModel.Retainer => "Monatlicher Betrag",
        _ => For(model),
    };

    /// <summary>Unit next to the amount: „€/h“, „€/Tag“, „€ fix“, „€/Monat“ (US-06 AK3).</summary>
    public static string AmountUnit(PricingModel model, string currency)
    {
        var symbol = Currency.Symbol(currency);
        return model switch
        {
            PricingModel.Hourly => $"{symbol}/h",
            PricingModel.Daily => $"{symbol}/Tag",
            PricingModel.FixedPrice => $"{symbol} fix",
            _ => $"{symbol}/Monat",
        };
    }

    public static string For(DurationUnit unit) => unit switch
    {
        DurationUnit.Days => "Tage",
        DurationUnit.Weeks => "Wochen",
        _ => "Monate",
    };

    public static string For(LostReason reason) => reason switch
    {
        LostReason.Price => "Preis",
        LostReason.Timing => "Zeitpunkt",
        LostReason.OtherCandidate => "Anderer Kandidat",
        LostReason.ProjectCancelled => "Projekt abgesagt",
        LostReason.NoResponse => "Keine Rückmeldung",
        LostReason.DeclinedByMe => "Von mir abgelehnt",
        _ => "Sonstiges",
    };

    public static string For(StageStatus status) => status switch
    {
        StageStatus.Open => "Offen",
        StageStatus.Won => "Gewonnen",
        _ => "Verloren",
    };
}
