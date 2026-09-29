using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Settings;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Web.Components.Pages.Settings;

namespace SoloCrm.Web.Tests.Components.Pages;

public sealed class PricingSettingsSectionTests : BunitContext
{
    private readonly ICommandHandler<UpdatePricingSettings.Command, UpdatePricingSettings.Result> _update =
        Substitute.For<ICommandHandler<UpdatePricingSettings.Command, UpdatePricingSettings.Result>>();

    public PricingSettingsSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var get = Substitute.For<IQueryHandler<GetSettings.Query, GetSettings.Result>>();
        get.Handle(Arg.Any<GetSettings.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetSettings.Result>.Success(new GetSettings.Result(PricingModel.Daily, "CHF", 7.5m, 6, 7)));
        Services.AddSingleton(get);
        Services.AddSingleton(_update);
    }

    [Fact]
    public async Task Submit_LoadedValues_SendsThemUnchanged()
    {
        _update.Handle(Arg.Any<UpdatePricingSettings.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<UpdatePricingSettings.Result>.Success(new UpdatePricingSettings.Result()));
        var section = Render<PricingSettingsSection>();

        await section.Find("form").SubmitAsync();

        await _update.Received(1).Handle(
            new UpdatePricingSettings.Command(PricingModel.Daily, "CHF", 7.5m, 6),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_ValidationError_ShowsMessageAtField()
    {
        _update.Handle(Arg.Any<UpdatePricingSettings.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<UpdatePricingSettings.Result>.Failure(new ValidationError(new Dictionary<string, string[]>
            {
                ["DefaultCurrency"] = ["Bitte einen gültigen Währungscode (ISO 4217, z. B. EUR) angeben."],
            })));
        var section = Render<PricingSettingsSection>();

        await section.Find("form").SubmitAsync();

        section.WaitForAssertion(() =>
            section.FindAll("label.mud-input-error").Select(l => l.TextContent.Trim()).Should().Equal("Standard-Währung"));
    }
}
