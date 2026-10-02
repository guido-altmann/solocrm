using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Domain.Common;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class ContactGdprMenuTests : BunitContext
{
    private static readonly Guid ContactId = Guid.CreateVersion7();

    private readonly IQueryHandler<ExportContactData.Query, ExportContactData.Result> _export =
        Substitute.For<IQueryHandler<ExportContactData.Query, ExportContactData.Result>>();

    private readonly IQueryHandler<GetContactErasure.Query, GetContactErasure.Result> _erasure =
        Substitute.For<IQueryHandler<GetContactErasure.Query, GetContactErasure.Result>>();

    private readonly BunitJSModuleInterop _download;

    private IRenderedComponent<MudBlazor.MudPopoverProvider> _popovers = null!;

    public ContactGdprMenuTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_export);
        Services.AddSingleton(_erasure);
        Services.AddSingleton(Substitute.For<ICommandHandler<DeleteContactPermanently.Command, DeleteContactPermanently.Result>>());
        Services.AddSingleton(new AppClock(
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 22, 30, 0, TimeSpan.Zero)), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
        JSInterop.Mode = JSRuntimeMode.Loose;
        _download = JSInterop.SetupModule("./js/download.js");
        _download.SetupVoid("downloadText", _ => true);
    }

    [Fact]
    public async Task Export_Clicked_DownloadsJsonNamedAfterContactAndLocalDate()
    {
        _export.Handle(new ExportContactData.Query(ContactId), Arg.Any<CancellationToken>()).Returns(Export());
        var menu = RenderMenu();

        await OpenMenuAsync(menu);
        _popovers.WaitForElement("[data-testid=contact-export]").Click();

        var call = _download.VerifyInvoke("downloadText");
        call.Arguments[0].Should().Be("kontakt-ada-lovelace-2026-10-03.json");
        call.Arguments[1].Should().Be("application/json");
        call.Arguments[2].As<string>().Should().Contain("\"formatVersion\": 1").And.Contain("\"email\": \"ada@example.test\"");
    }

    [Fact]
    public async Task Export_UnknownContact_ShowsErrorWithoutDownload()
    {
        _export.Handle(Arg.Any<ExportContactData.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExportContactData.Result>.Failure(ContactErrors.NotFound));
        var menu = RenderMenu();

        await OpenMenuAsync(menu);
        _popovers.WaitForElement("[data-testid=contact-export]").Click();

        _download.Invocations.Should().NotContain(i => i.Identifier == "downloadText");
    }

    [Fact]
    public async Task Erase_Confirmed_NavigatesToContactList()
    {
        _erasure.Handle(new GetContactErasure.Query(ContactId), Arg.Any<CancellationToken>())
            .Returns(new GetContactErasure.Result(ContactId, "Ada Lovelace", 0, 0, []));
        var delete = Services.GetRequiredService<ICommandHandler<DeleteContactPermanently.Command, DeleteContactPermanently.Result>>();
        delete.Handle(Arg.Any<DeleteContactPermanently.Command>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteContactPermanently.Result(ContactId, 0, 0, 0));
        var dialogs = Render<MudBlazor.MudDialogProvider>();
        var menu = RenderMenu();

        await OpenMenuAsync(menu);
        _popovers.WaitForElement("[data-testid=contact-erase]").Click();
        await dialogs.WaitForElement("input[data-testid=erasure-confirmation]").InputAsync(new() { Value = "Ada Lovelace" });
        dialogs.Find("[data-testid=erasure-submit]").Click();

        menu.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/contacts"));
    }

    private IRenderedComponent<ContactGdprMenu> RenderMenu()
    {
        _popovers = Render<MudBlazor.MudPopoverProvider>();
        return Render<ContactGdprMenu>(p => p.Add(m => m.ContactId, ContactId).Add(m => m.Name, "Ada Lovelace"));
    }

    private static async Task OpenMenuAsync(IRenderedComponent<ContactGdprMenu> menu) =>
        await menu.Find("[data-testid=contact-actions] button").ClickAsync(new());

    private static ExportContactData.Result Export() => new(
        1,
        DateTimeOffset.UnixEpoch,
        new ExportContactData.ContactData(
            ContactId, "Ada", "Lovelace", "ada@example.test", null, null, null, LeadSource.Referral, false,
            new ExportContactData.AddressExport(null, null, null, null, null, null), new Dictionary<string, string>(), null, [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        [],
        [],
        [],
        []);
}
