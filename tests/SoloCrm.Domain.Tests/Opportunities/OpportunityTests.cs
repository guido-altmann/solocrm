using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Domain.Tests.Opportunities;

public sealed class OpportunityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly Stage _new = Stage.Create("Neu", 1, StageStatus.Open);
    private readonly Stage _talks = Stage.Create("Im Gespräch", 2, StageStatus.Open);
    private readonly Stage _won = Stage.Create("Gewonnen", 3, StageStatus.Won);
    private readonly Stage _lost = Stage.Create("Verloren", 4, StageStatus.Lost);
    private readonly Stage _lostToo = Stage.Create("Abgesagt", 5, StageStatus.Lost);

    [Fact]
    public void Create_TitleOnly_StartsOpenAndRaisesCreated()
    {
        var opportunity = Opportunity.Create(" Migration Azure ", _new);

        opportunity.Title.Should().Be("Migration Azure");
        opportunity.StageId.Should().Be(_new.Id);
        opportunity.Status.Should().Be(StageStatus.Open);
        opportunity.Pricing.Should().BeNull();
        opportunity.ClosedAt.Should().BeNull();
        opportunity.DomainEvents.Should().ContainSingle().Which.Should().Be(new OpportunityCreated(opportunity.Id, _new.Id));
    }

    [Fact]
    public void Create_ClosedStage_Throws()
    {
        var act = () => Opportunity.Create("Migration", _won);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_BlankTitle_Throws(string title)
    {
        var act = () => Opportunity.Create(title, _new);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(101, null)]
    [InlineData(null, -1)]
    [InlineData(null, 101)]
    public void Create_PercentageOutOfRange_Throws(int? utilization, int? remote)
    {
        var act = () => Opportunity.Create("Migration", _new, new OpportunityDetails(Utilization: utilization, RemotePercentage: remote));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PricingModel.Hourly, 80)]
    [InlineData(PricingModel.Daily, 80)]
    [InlineData(PricingModel.FixedPrice, null)]
    [InlineData(PricingModel.Retainer, null)]
    public void Update_PricingModel_KeepsUtilizationOnlyForRates(PricingModel model, int? expected)
    {
        var opportunity = Opportunity.Create("Migration", _new);

        opportunity.Update("Migration", new OpportunityDetails(Pricing: Pricing.Create(model, 100m), Utilization: 80, Source: LeadSource.Referral));

        opportunity.Utilization.Should().Be(expected);
        opportunity.Source.Should().Be(LeadSource.Referral);
    }

    [Fact]
    public void EstimatedValue_WithPricingAndDuration_UsesValuation()
    {
        var opportunity = Opportunity.Create("Migration", _new, new OpportunityDetails(
            Pricing: Pricing.Create(PricingModel.Daily, 800m), Duration: Duration.Create(2, DurationUnit.Weeks), Utilization: 50));

        opportunity.EstimatedValue(ValuationSettings.Default).Should().Be(4_000m);
        opportunity.MonthlyRecurringValue().Should().BeNull();
    }

    [Fact]
    public void ChangeStage_ToOpenStage_RaisesEventWithoutClosing()
    {
        var opportunity = CreateOpen();

        opportunity.ChangeStage(_talks, null, Now);

        opportunity.StageId.Should().Be(_talks.Id);
        opportunity.ClosedAt.Should().BeNull();
        opportunity.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new OpportunityStageChanged(opportunity.Id, _new.Id, _talks.Id, StageStatus.Open));
    }

    [Fact]
    public void ChangeStage_ToWon_SetsClosedAt()
    {
        var opportunity = CreateOpen();

        opportunity.ChangeStage(_won, null, Now);

        opportunity.ClosedAt.Should().Be(Now);
        opportunity.LostReason.Should().BeNull();
        opportunity.Status.Should().Be(StageStatus.Won);
    }

    [Fact]
    public void ChangeStage_ToLostWithReason_SetsClosedAtAndReason()
    {
        var opportunity = CreateOpen();

        opportunity.ChangeStage(_lost, LostReason.Price, Now);

        opportunity.ClosedAt.Should().Be(Now);
        opportunity.LostReason.Should().Be(LostReason.Price);
        opportunity.Status.Should().Be(StageStatus.Lost);
        opportunity.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new OpportunityStageChanged(opportunity.Id, _new.Id, _lost.Id, StageStatus.Lost));
    }

    [Fact]
    public void ChangeStage_ToLostWithoutReason_ThrowsAndKeepsState()
    {
        var opportunity = CreateOpen();

        var act = () => opportunity.ChangeStage(_lost, null, Now);

        act.Should().Throw<InvalidOperationException>();
        opportunity.StageId.Should().Be(_new.Id);
        opportunity.ClosedAt.Should().BeNull();
        opportunity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangeStage_ReopenLost_ClearsClosedAtAndReason()
    {
        var opportunity = CreateOpen();
        opportunity.ChangeStage(_lost, LostReason.Timing, Now);
        opportunity.ClearDomainEvents();

        opportunity.ChangeStage(_talks, null, Now.AddDays(3));

        opportunity.ClosedAt.Should().BeNull();
        opportunity.LostReason.Should().BeNull();
        opportunity.Status.Should().Be(StageStatus.Open);
        opportunity.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new OpportunityStageChanged(opportunity.Id, _lost.Id, _talks.Id, StageStatus.Open));
    }

    [Fact]
    public void ChangeStage_WonToLost_RequiresReasonAndResetsClosedAt()
    {
        var opportunity = CreateOpen();
        opportunity.ChangeStage(_won, null, Now);

        var withoutReason = () => opportunity.ChangeStage(_lost, null, Now.AddDays(1));
        withoutReason.Should().Throw<InvalidOperationException>();

        opportunity.ChangeStage(_lost, LostReason.ProjectCancelled, Now.AddDays(1));
        opportunity.ClosedAt.Should().Be(Now.AddDays(1));
        opportunity.LostReason.Should().Be(LostReason.ProjectCancelled);
    }

    [Fact]
    public void ChangeStage_BetweenLostStages_KeepsClosedAtAndReason()
    {
        var opportunity = CreateOpen();
        opportunity.ChangeStage(_lost, LostReason.NoResponse, Now);

        opportunity.ChangeStage(_lostToo, null, Now.AddDays(1));

        opportunity.StageId.Should().Be(_lostToo.Id);
        opportunity.ClosedAt.Should().Be(Now);
        opportunity.LostReason.Should().Be(LostReason.NoResponse);
    }

    [Fact]
    public void ChangeStage_SameLostStageWithNewReason_UpdatesReasonWithoutEvent()
    {
        var opportunity = CreateOpen();
        opportunity.ChangeStage(_lost, LostReason.NoResponse, Now);
        opportunity.ClearDomainEvents();

        opportunity.ChangeStage(_lost, LostReason.DeclinedByMe, Now.AddDays(1));

        opportunity.LostReason.Should().Be(LostReason.DeclinedByMe);
        opportunity.ClosedAt.Should().Be(Now);
        opportunity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangeStage_SameOpenStage_DoesNothing()
    {
        var opportunity = CreateOpen();

        opportunity.ChangeStage(_new, null, Now);

        opportunity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ChangeStage_UnknownReason_Throws()
    {
        var opportunity = CreateOpen();

        var act = () => opportunity.ChangeStage(_lost, (LostReason)42, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private Opportunity CreateOpen()
    {
        var opportunity = Opportunity.Create("Migration Azure", _new);
        opportunity.ClearDomainEvents();
        return opportunity;
    }
}
