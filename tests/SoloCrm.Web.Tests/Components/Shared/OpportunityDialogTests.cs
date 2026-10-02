using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class OpportunityDialogTests : BunitContext
{
    private static readonly Guid NewStage = Guid.CreateVersion7();

    // 23:30 UTC on 2 October is already 3 October in Berlin.
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 23, 30, 0, TimeSpan.Zero);

    private readonly ICommandHandler<CreateOpportunity.Command, CreateOpportunity.Result> _create =
        Substitute.For<ICommandHandler<CreateOpportunity.Command, CreateOpportunity.Result>>();

    private readonly IQueryHandler<GetOpportunityDefaults.Query, GetOpportunityDefaults.Result> _defaults =
        Substitute.For<IQueryHandler<GetOpportunityDefaults.Query, GetOpportunityDefaults.Result>>();

    private IRenderedComponent<MudPopoverProvider>? _popovers;

    public OpportunityDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(new AppClock(new FakeTimeProvider(Now), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
        Services.AddSingleton(_create);
        Services.AddSingleton(_defaults);
        Services.AddSingleton(Substitute.For<ICommandHandler<UpdateOpportunity.Command, UpdateOpportunity.Result>>());
        Services.AddSingleton(Substitute.For<ICommandHandler<ArchiveOpportunity.Command, ArchiveOpportunity.Result>>());
        Services.AddSingleton(Substitute.For<ICommandHandler<RestoreOpportunity.Command, RestoreOpportunity.Result>>());
        Services.AddSingleton(Substitute.For<IQueryHandler<GetOpportunity.Query, GetOpportunity.Result>>());
        Services.AddSingleton(Substitute.For<IQueryHandler<SearchOrganizations.Query, SearchOrganizations.Result>>());
        Services.AddSingleton(Substitute.For<IQueryHandler<GetContacts.Query, GetContacts.Result>>());
        UseDefaults(PricingModel.Hourly);
    }

    [Theory]
    [InlineData(PricingModel.Hourly, "Stundensatz", "€/h", true)]
    [InlineData(PricingModel.Daily, "Tagessatz", "€/Tag", true)]
    [InlineData(PricingModel.FixedPrice, "Festpreis", "€ fix", false)]
    [InlineData(PricingModel.Retainer, "Monatlicher Betrag", "€/Monat", false)]
    public async Task Open_PricingModel_AdaptsAmountLabelUnitAndUtilization(PricingModel model, string label, string unit, bool utilization)
    {
        UseDefaults(model);

        var provider = await OpenDialogAsync();

        LabelOf(provider, "amount").Should().Be(label);
        FieldOf(provider, "amount").TextContent.Should().Contain(unit);
        provider.FindAll("[data-test=utilization]").Should().HaveCount(utilization ? 1 : 0);
    }

    [Fact]
    public async Task SelectPricingModel_FixedPrice_HidesUtilizationAndChangesLabel()
    {
        var provider = await OpenDialogAsync();
        provider.FindAll("[data-test=utilization]").Should().ContainSingle();

        await FieldOf(provider, "pricing-model").MouseDownAsync(new());
        _popovers!.WaitForElements(".mud-list-item");
        _popovers!.FindAll(".mud-list-item").Single(i => i.TextContent.Contains("Festpreis", StringComparison.Ordinal)).Click();

        provider.WaitForAssertion(() =>
        {
            provider.FindAll("[data-test=utilization]").Should().BeEmpty();
            LabelOf(provider, "amount").Should().Be("Festpreis");
        });
    }

    [Fact]
    public async Task EnterAmountAndDuration_Hourly_ShowsLiveEstimatedValue()
    {
        var provider = await OpenDialogAsync();
        provider.Find("[data-test=estimated-value]").TextContent.Should().Contain("–");

        provider.Find("[data-test=amount]").Input("100");
        provider.Find("[data-test=duration-value]").Input("1");
        provider.Find("[data-test=utilization]").Input("50");

        // 100 €/h × 8 h × 20 days × 50 %
        provider.WaitForAssertion(() =>
            provider.Find("[data-test=estimated-value]").TextContent.Should().Contain("8.000 €"));
    }

    [Fact]
    public async Task EnterAmount_RetainerWithoutDuration_ShowsValueAndMrr()
    {
        UseDefaults(PricingModel.Retainer);
        var provider = await OpenDialogAsync();

        provider.Find("[data-test=amount]").Input("2500");

        provider.WaitForAssertion(() =>
        {
            provider.Find("[data-test=estimated-value]").TextContent.Should().Contain("30.000 €");
            provider.Find("[data-test=mrr]").TextContent.Should().Contain("2.500 €/Monat");
        });
    }

    [Fact]
    public async Task Submit_TitleAndAmount_SendsCreateWithDefaults()
    {
        var id = Guid.CreateVersion7();
        _create.Handle(Arg.Any<CreateOpportunity.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateOpportunity.Result>.Success(new CreateOpportunity.Result(id)));
        var provider = await OpenDialogAsync();

        provider.Find("input").Input("Migration Azure");
        provider.Find("[data-test=amount]").Input("95");
        await provider.Find("form").SubmitAsync();

        await _create.Received(1).Handle(
            Arg.Is<CreateOpportunity.Command>(c =>
                c.Title == "Migration Azure" && c.StageId == NewStage && c.PricingModel == PricingModel.Hourly
                && c.Amount == 95m && c.Currency == "EUR" && c.DurationValue == null && c.DurationUnit == null
                && c.ReceivedOn == new DateOnly(2026, 10, 3)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Open_NewRequest_PresetsReceivedOnWithTodayInConfiguredTimeZone()
    {
        var provider = await OpenDialogAsync();

        provider.Find("input[data-test=received-on]").GetAttribute("value").Should().Be("03.10.2026");
    }

    /// <summary>MudBlazor renders additional attributes on the input; label and adornment live in its control.</summary>
    private static AngleSharp.Dom.IElement FieldOf(IRenderedComponent<MudDialogProvider> provider, string test) =>
        provider.Find($"[data-test={test}]").Closest(".mud-input-control")!;

    private static string LabelOf(IRenderedComponent<MudDialogProvider> provider, string test) =>
        FieldOf(provider, test).QuerySelector("label")!.TextContent;

    private void UseDefaults(PricingModel model) =>
        _defaults.Handle(Arg.Any<GetOpportunityDefaults.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetOpportunityDefaults.Result>.Success(new GetOpportunityDefaults.Result(
                [
                    new GetOpportunityDefaults.StageItem(NewStage, "Neu", StageStatus.Open),
                    new GetOpportunityDefaults.StageItem(Guid.CreateVersion7(), "Gewonnen", StageStatus.Won),
                ],
                model,
                "EUR",
                ValuationSettings.Default)));

    private async Task<IRenderedComponent<MudDialogProvider>> OpenDialogAsync()
    {
        var provider = Render<MudDialogProvider>();
        _popovers = Render<MudPopoverProvider>();
        await provider.InvokeAsync(() =>
            Services.GetRequiredService<IDialogService>().ShowAsync<OpportunityDialog>("Neue Anfrage"));
        provider.WaitForElement("[data-test=amount]");
        return provider;
    }
}
