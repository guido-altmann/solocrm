using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class QuickAddContactDialogTests : BunitContext
{
    private readonly ICommandHandler<CreateContact.Command, CreateContact.Result> _handler =
        Substitute.For<ICommandHandler<CreateContact.Command, CreateContact.Result>>();

    public QuickAddContactDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_handler);
    }

    [Fact]
    public async Task Submit_MissingName_ShowsValidationErrorAtLastName()
    {
        _handler.Handle(Arg.Any<CreateContact.Command>(), Arg.Any<CancellationToken>())
            .Returns(Fail(new ValidationError(new Dictionary<string, string[]>
            {
                [nameof(CreateContact.Command.LastName)] = ["Bitte Vor- oder Nachnamen angeben."],
            })));
        var (provider, _) = await OpenDialogAsync();

        await provider.Find("form").SubmitAsync();

        provider.WaitForAssertion(() =>
            provider.Markup.Should().Contain("Bitte Vor- oder Nachnamen angeben."));
        ErrorLabels(provider).Should().Equal("Nachname");
    }

    [Fact]
    public async Task Submit_DuplicateEmail_ShowsErrorAtEmail()
    {
        _handler.Handle(Arg.Any<CreateContact.Command>(), Arg.Any<CancellationToken>())
            .Returns(Fail(CreateContact.Errors.DuplicateEmail));
        var (provider, _) = await OpenDialogAsync();
        provider.Find("input[type=email]").Input("ada@example.test");

        await provider.Find("form").SubmitAsync();

        provider.WaitForAssertion(() =>
            provider.Markup.Should().Contain(CreateContact.Errors.DuplicateEmail.Message));
        ErrorLabels(provider).Should().Equal("E-Mail");
        await _handler.Received(1).Handle(
            Arg.Is<CreateContact.Command>(c => c.Email == "ada@example.test"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_Success_ClosesDialogWithContactId()
    {
        var id = Guid.CreateVersion7();
        _handler.Handle(Arg.Any<CreateContact.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateContact.Result>.Success(new CreateContact.Result(id)));
        var (provider, dialog) = await OpenDialogAsync();
        provider.Find("input").Input("Ada");

        await provider.Find("form").SubmitAsync();

        var result = await dialog.Result;
        result!.Canceled.Should().BeFalse();
        result.Data.Should().Be(id);
    }

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> OpenDialogAsync()
    {
        var provider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var dialog = await provider.InvokeAsync(() =>
            Services.GetRequiredService<IDialogService>().ShowAsync<QuickAddContactDialog>("Neuer Kontakt"));
        return (provider, dialog);
    }

    private static IEnumerable<string> ErrorLabels(IRenderedComponent<MudDialogProvider> provider) =>
        provider.FindAll("label.mud-input-error").Select(l => l.TextContent.Trim());

    private static Task<Result<CreateContact.Result>> Fail(Error error) =>
        Task.FromResult(Result<CreateContact.Result>.Failure(error));
}
