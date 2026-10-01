using SoloCrm.Application.Features.Common;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Organizations;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class OrganizationDialogTests : BunitContext
{
    private readonly ICommandHandler<CreateOrganization.Command, CreateOrganization.Result> _create =
        Substitute.For<ICommandHandler<CreateOrganization.Command, CreateOrganization.Result>>();

    private readonly ICommandHandler<UpdateOrganization.Command, UpdateOrganization.Result> _update =
        Substitute.For<ICommandHandler<UpdateOrganization.Command, UpdateOrganization.Result>>();

    private readonly IQueryHandler<GetOrganization.Query, GetOrganization.Result> _get =
        Substitute.For<IQueryHandler<GetOrganization.Query, GetOrganization.Result>>();

    public OrganizationDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_create);
        Services.AddSingleton(_update);
        Services.AddSingleton(_get);
    }

    [Fact]
    public async Task Open_CreateMode_HidesOptionalFieldsUntilRequested()
    {
        var (provider, _) = await OpenDialogAsync(null);

        provider.FindAll("input").Should().HaveCount(2, "name and type only");
        provider.FindAll("button").Single(b => b.TextContent.Contains("Weitere Angaben", StringComparison.Ordinal)).Click();

        provider.WaitForAssertion(() => provider.Markup.Should().Contain("Website"));
    }

    [Fact]
    public async Task Submit_ValidationError_ShowsMessageAtWebsite()
    {
        _create.Handle(Arg.Any<CreateOrganization.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateOrganization.Result>.Failure(new ValidationError(new Dictionary<string, string[]>
            {
                ["Website"] = ["Bitte eine gültige Website angeben (z. B. example.com)."],
            })));
        var (provider, _) = await OpenDialogAsync(null);
        provider.FindAll("button").Single(b => b.TextContent.Contains("Weitere Angaben", StringComparison.Ordinal)).Click();

        await provider.Find("form").SubmitAsync();

        provider.WaitForAssertion(() =>
            provider.FindAll("label.mud-input-error").Select(l => l.TextContent.Trim()).Should().Equal("Website"));
    }

    [Fact]
    public async Task Submit_EditMode_LoadsValuesAndSendsUpdate()
    {
        var id = Guid.CreateVersion7();
        _get.Handle(new GetOrganization.Query(id), Arg.Any<CancellationToken>())
            .Returns(Result<GetOrganization.Result>.Success(
                new GetOrganization.Result(id, "Contoso", OrganizationType.Client, "https://contoso.de", new AddressData(City: "Berlin", CountryCode: "DE"), null, false)));
        _update.Handle(Arg.Any<UpdateOrganization.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<UpdateOrganization.Result>.Success(new UpdateOrganization.Result(id)));
        var (provider, dialog) = await OpenDialogAsync(id);
        provider.WaitForAssertion(() => provider.Find("input").GetAttribute("value").Should().Be("Contoso"));

        provider.Find("input").Input("Contoso AG");
        await provider.Find("form").SubmitAsync();

        (await dialog.Result)!.Data.Should().Be(id);
        await _update.Received(1).Handle(
            new UpdateOrganization.Command(id, "Contoso AG", OrganizationType.Client, "https://contoso.de", new AddressData(City: "Berlin", CountryCode: "DE"), null),
            Arg.Any<CancellationToken>());
        await _create.DidNotReceiveWithAnyArgs().Handle(default!, Xunit.TestContext.Current.CancellationToken);
    }

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> OpenDialogAsync(Guid? id)
    {
        var provider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var parameters = new DialogParameters<OrganizationDialog> { { d => d.OrganizationId, id } };
        var dialog = await provider.InvokeAsync(() =>
            Services.GetRequiredService<IDialogService>().ShowAsync<OrganizationDialog>("Organisation", parameters));
        return (provider, dialog);
    }
}
