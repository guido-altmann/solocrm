using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class ContactErasureDialogTests : BunitContext
{
    private static readonly Guid ContactId = Guid.CreateVersion7();

    private readonly ICommandHandler<DeleteContactPermanently.Command, DeleteContactPermanently.Result> _delete =
        Substitute.For<ICommandHandler<DeleteContactPermanently.Command, DeleteContactPermanently.Result>>();

    public ContactErasureDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_delete);
        _delete.Handle(Arg.Any<DeleteContactPermanently.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeleteContactPermanently.Result>.Success(new DeleteContactPermanently.Result(ContactId, 3, 1, 1)));
    }

    [Fact]
    public async Task Open_Always_NamesConsequencesAndAffectedOpportunities()
    {
        var (provider, _) = await OpenDialogAsync();

        provider.Find("[data-testid=erasure-consequences]").TextContent.Should()
            .Contain("„Ada Lovelace“").And.Contain("3 Aktivitäten").And.Contain("1 Aufgabe");
        provider.Find("[data-testid=erasure-opportunities]").TextContent.Should().Contain("Migration Azure");
        provider.Markup.Should().Contain("kann nicht wiederhergestellt werden").And.Contain("30 Tagen");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ada")]
    [InlineData("ada lovelace")]
    public async Task Confirm_NameDoesNotMatch_KeepsButtonDisabledAndDoesNotDelete(string typed)
    {
        var (provider, dialog) = await OpenDialogAsync();

        await provider.Find("input[data-testid=erasure-confirmation]").InputAsync(new() { Value = typed });

        provider.Find("[data-testid=erasure-submit]").HasAttribute("disabled").Should().BeTrue();
        await _delete.DidNotReceive().Handle(Arg.Any<DeleteContactPermanently.Command>(), Arg.Any<CancellationToken>());
        dialog.Result.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_NameMatches_DeletesAndClosesWithResult()
    {
        var (provider, dialog) = await OpenDialogAsync();

        await provider.Find("input[data-testid=erasure-confirmation]").InputAsync(new() { Value = "Ada Lovelace" });
        provider.Find("[data-testid=erasure-submit]").Click();

        var result = await dialog.Result;
        result!.Canceled.Should().BeFalse();
        await _delete.Received(1).Handle(new DeleteContactPermanently.Command(ContactId, "Ada Lovelace"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_HandlerRejects_ShowsErrorAndStaysOpen()
    {
        _delete.Handle(Arg.Any<DeleteContactPermanently.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeleteContactPermanently.Result>.Failure(ContactErrors.ErasureNotConfirmed));
        var (provider, dialog) = await OpenDialogAsync();

        await provider.Find("input[data-testid=erasure-confirmation]").InputAsync(new() { Value = "Ada Lovelace" });
        provider.Find("[data-testid=erasure-submit]").Click();

        provider.WaitForAssertion(() => provider.Markup.Should().Contain("stimmt nicht mit dem Kontakt überein"));
        dialog.Result.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Cancel_Always_ReturnsCanceledWithoutDeleting()
    {
        var (provider, dialog) = await OpenDialogAsync();

        provider.FindAll("button").Single(b => b.TextContent.Contains("Abbrechen", StringComparison.Ordinal)).Click();

        (await dialog.Result)!.Canceled.Should().BeTrue();
        await _delete.DidNotReceive().Handle(Arg.Any<DeleteContactPermanently.Command>(), Arg.Any<CancellationToken>());
    }

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> OpenDialogAsync()
    {
        var provider = Render<MudDialogProvider>();
        var erasure = new GetContactErasure.Result(ContactId, "Ada Lovelace", 3, 1, [new RecordRef(Guid.CreateVersion7(), "Migration Azure")]);
        var parameters = new DialogParameters<ContactErasureDialog> { { d => d.Erasure, erasure } };
        var dialog = await provider.InvokeAsync(() =>
            Services.GetRequiredService<IDialogService>().ShowAsync<ContactErasureDialog>("Kontakt endgültig löschen", parameters));
        return (provider, dialog);
    }
}
