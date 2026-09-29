using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Domain.Opportunities;

/// <summary>
/// A project request or offer that moves through the pipeline (SPEC 2.3).
/// </summary>
public sealed class Opportunity : ArchivableEntity, IAuditable, IHasExtraFields
{
    public const int TitleMaxLength = 200;

    // Required by EF Core.
    private Opportunity()
    {
        Title = null!;
    }

    private Opportunity(string title, Guid stageId)
    {
        Title = title;
        StageId = stageId;
    }

    public string Title { get; private set; }

    public Guid StageId { get; private set; }

    public Stage? Stage { get; private set; }

    /// <summary>End client the project is carried out for; may be unknown at first.</summary>
    public Guid? ClientOrganizationId { get; private set; }

    public Organization? ClientOrganization { get; private set; }

    /// <summary>Agency between freelancer and client (optional).</summary>
    public Guid? AgencyOrganizationId { get; private set; }

    public Organization? AgencyOrganization { get; private set; }

    public Guid? PrimaryContactId { get; private set; }

    public Contact? PrimaryContact { get; private set; }

    public Pricing? Pricing { get; private set; }

    public DateOnly? StartDate { get; private set; }

    /// <summary><c>null</c> means open-ended.</summary>
    public Duration? Duration { get; private set; }

    /// <summary>Utilization in percent (0–100); only relevant for hourly and daily rates.</summary>
    public int? Utilization { get; private set; }

    public int? RemotePercentage { get; private set; }

    public LeadSource? Source { get; private set; }

    /// <summary>Set only while the opportunity is in a <see cref="StageStatus.Lost"/> stage.</summary>
    public LostReason? LostReason { get; private set; }

    /// <summary>Set when moving to a won or lost stage; cleared when reopened.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyDictionary<string, string> ExtraFields { get; private set; } = new Dictionary<string, string>();

    /// <summary>Status of the current stage, derived from <see cref="ClosedAt"/> and <see cref="LostReason"/>.</summary>
    public StageStatus Status => ClosedAt is null
        ? StageStatus.Open
        : LostReason is null ? StageStatus.Won : StageStatus.Lost;

    /// <exception cref="ArgumentException">Blank title, stage not open or invalid details.</exception>
    public static Opportunity Create(string title, Stage stage, OpportunityDetails? details = null)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (stage.Status != StageStatus.Open)
        {
            throw new ArgumentException("New opportunities start in an open stage.", nameof(stage));
        }

        var opportunity = new Opportunity(RequireTitle(title), stage.Id);
        opportunity.Apply(details ?? new OpportunityDetails());
        opportunity.AddDomainEvent(new OpportunityCreated(opportunity.Id, stage.Id));
        return opportunity;
    }

    /// <exception cref="ArgumentException">Blank title or invalid details.</exception>
    public void Update(string title, OpportunityDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        Title = RequireTitle(title);
        Apply(details);
    }

    /// <summary>
    /// Moves the opportunity to <paramref name="stage"/>. Won/lost set <see cref="ClosedAt"/>; a lost stage requires
    /// a reason (an existing one is kept when moving between lost stages). Moving back to an open stage reopens the
    /// opportunity and clears <see cref="ClosedAt"/> and <see cref="LostReason"/>. Staying in the same lost stage only
    /// updates the reason.
    /// </summary>
    /// <exception cref="InvalidOperationException">Lost stage without a reason.</exception>
    public void ChangeStage(Stage stage, LostReason? lostReason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (lostReason is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(lostReason), lostReason, "Unknown lost reason.");
        }

        if (stage.Id == StageId)
        {
            if (stage.Status == StageStatus.Lost && lostReason is not null)
            {
                LostReason = lostReason;
            }

            return;
        }

        var previousStatus = Status;
        var fromStageId = StageId;

        switch (stage.Status)
        {
            case StageStatus.Open:
                ClosedAt = null;
                LostReason = null;
                break;

            case StageStatus.Won:
                ClosedAt = previousStatus == StageStatus.Won ? ClosedAt : now;
                LostReason = null;
                break;

            default:
                var reason = lostReason ?? (previousStatus == StageStatus.Lost ? LostReason : null);
                LostReason = reason ?? throw new InvalidOperationException("Moving to a lost stage requires a lost reason.");
                ClosedAt = previousStatus == StageStatus.Lost ? ClosedAt : now;
                break;
        }

        StageId = stage.Id;
        AddDomainEvent(new OpportunityStageChanged(Id, fromStageId, stage.Id, stage.Status));
    }

    /// <summary>
    /// Whether moving to <paramref name="stage"/> needs a new lost reason: the stage is lost and the
    /// opportunity is not lost yet (between lost stages the existing reason is kept).
    /// </summary>
    public bool RequiresLostReason(Stage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return stage.Status == StageStatus.Lost && Status != StageStatus.Lost;
    }

    public decimal? EstimatedValue(ValuationSettings settings) =>
        OpportunityValuation.EstimatedValue(Pricing, Duration, Utilization, settings);

    public decimal? MonthlyRecurringValue() => OpportunityValuation.MonthlyRecurringValue(Pricing);

    /// <summary>Whether <see cref="Utilization"/> applies to the pricing model (hourly or daily rate, or no pricing yet).</summary>
    public static bool UsesUtilization(PricingModel? model) => model is null or PricingModel.Hourly or PricingModel.Daily;

    private void Apply(OpportunityDetails details)
    {
        RequirePercentage(details.Utilization, nameof(details.Utilization));
        RequirePercentage(details.RemotePercentage, nameof(details.RemotePercentage));
        if (details.Source is { } source && !Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(details), source, "Unknown lead source.");
        }

        ClientOrganizationId = details.ClientOrganizationId;
        AgencyOrganizationId = details.AgencyOrganizationId;
        PrimaryContactId = details.PrimaryContactId;
        Pricing = details.Pricing;
        StartDate = details.StartDate;
        Duration = details.Duration;
        Utilization = UsesUtilization(details.Pricing?.Model) ? details.Utilization : null;
        RemotePercentage = details.RemotePercentage;
        Source = details.Source;
    }

    private static void RequirePercentage(int? value, string name)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(name, value, "Percentages must be between 0 and 100.");
        }
    }

    private static string RequireTitle(string title) =>
        string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("An opportunity requires a title.", nameof(title))
            : title.Trim();
}
