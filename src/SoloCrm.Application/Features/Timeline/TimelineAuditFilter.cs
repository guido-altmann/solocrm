using System.Globalization;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Timeline;

/// <summary>
/// Which audit changes the timeline shows and how (SPEC 2.5, ADR-006). All other changes stay audited but invisible.
/// </summary>
public static class TimelineAuditFilter
{
    public const string StageField = nameof(Opportunity.StageId);
    public const string OrganizationField = "OrganizationId";
    public const string PricingPrefix = nameof(Opportunity.Pricing) + ".";
    public const string DurationPrefix = nameof(Opportunity.Duration) + ".";

    /// <summary>The configured visible fields; complex types by prefix.</summary>
    public static IReadOnlySet<string> VisibleFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        StageField,
        PricingPrefix + nameof(Pricing.Model),
        PricingPrefix + nameof(Pricing.Amount),
        PricingPrefix + nameof(Pricing.Currency),
        DurationPrefix + nameof(Duration.Value),
        DurationPrefix + nameof(Duration.Unit),
        OrganizationField,
        nameof(ArchivableEntity.IsArchived),
    };

    public static bool IsVisible(AuditChange change) => VisibleFields.Contains(change.Field);

    /// <summary>Whether showing this entry needs the full pricing/duration state (the diff only has changed members).</summary>
    public static bool NeedsState(IEnumerable<AuditChange> changes) =>
        changes.Any(c => c.Field.StartsWith(PricingPrefix, StringComparison.Ordinal)
            || c.Field.StartsWith(DurationPrefix, StringComparison.Ordinal));

    /// <summary>„95 €/h“; „–“ without pricing. Falls back to the raw amount if the state is incomplete.</summary>
    public static string FormatPricing(IReadOnlyDictionary<string, string?> state)
    {
        var amount = state.GetValueOrDefault(PricingPrefix + nameof(Pricing.Amount));
        if (amount is null)
        {
            return Missing;
        }

        var model = state.GetValueOrDefault(PricingPrefix + nameof(Pricing.Model));
        var currency = state.GetValueOrDefault(PricingPrefix + nameof(Pricing.Currency));
        if (Enum.TryParse<PricingModel>(model, out var pricingModel)
            && Enum.IsDefined(pricingModel)
            && decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            && currency is not null
            && Currency.IsValid(currency))
        {
            return Pricing.Create(pricingModel, value, currency).ToDisplayString();
        }

        return amount;
    }

    /// <summary>„6 Monate“; „offen“ without duration.</summary>
    public static string FormatDuration(IReadOnlyDictionary<string, string?> state)
    {
        var value = state.GetValueOrDefault(DurationPrefix + nameof(Duration.Value));
        if (value is null)
        {
            return "offen";
        }

        var unit = state.GetValueOrDefault(DurationPrefix + nameof(Duration.Unit));
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && Enum.TryParse<DurationUnit>(unit, out var durationUnit)
            && Enum.IsDefined(durationUnit)
            && number is > 0 and <= Duration.MaxValue
                ? Duration.Create(number, durationUnit).ToDisplayString()
                : value;
    }

    public const string Missing = "–";

    public const string Deleted = "(gelöscht)";
}
