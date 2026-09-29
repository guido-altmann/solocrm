using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Organizations;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class ContactDialogTests : BunitContext
{
    private readonly ICommandHandler<CreateContact.Command, CreateContact.Result> _create =
        Substitute.For<ICommandHandler<CreateContact.Command, CreateContact.Result>>();

    private readonly ICommandHandler<UpdateContact.Command, UpdateContact.Result> _update =
        Substitute.For<ICommandHandler<UpdateContact.Command, UpdateContact.Result>>();

    private readonly IQueryHandler<GetContact.Query, GetContact.Result> _get =
        Substitute.For<IQueryHandler<GetContact.Query, GetContact.Result>>();

    private readonly IQueryHandler<SearchOrganizations.Query, SearchOrganizations.Result> _searchOrganizations =
        Substitute.For<IQueryHandler<SearchOrganizations.Query, SearchOrganizations.Result>>();

    public ContactDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_create);
        Services.AddSingleton(_update);
        Services.AddSingleton(_get);
        Services.AddSingleton(_searchOrganizations);
        _searchOrganizations.Handle(Arg.Any<SearchOrganizations.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchOrganizations.Result>.Success(new SearchOrganizations.Result([])));
    }

    [Fact]
    public async Task Submit_MissingName_ShowsValidationErrorAtLastName()
    {
        _create.Handle(Arg.Any<CreateContact.Command>(), Arg.Any<CancellationToken>())
            .Returns(Fail<CreateContact.Result>(new ValidationError(new Dictionary<string, string[]>
            {
                [nameof(CreateContact.Command.LastName)] = ["Bitte Vor- oder Nachnamen angeben."],
            })));
        var (provider, _) = await OpenDialogAsync(null);

        await provider.Find("form").SubmitAsync();

        provider.WaitForAssertion(() =>
            provider.Markup.Should().Contain("Bitte Vor- oder Nachnamen angeben."));
        ErrorLabels(provider).Should().Equal("Nachname");
    }

    [Fact]
    public async Task Submit_DuplicateEmail_ShowsErrorAtEmail()
    {
        _create.Handle(Arg.Any<CreateContact.Command>(), Arg.Any<CancellationToken>())
            .Returns(Fail<CreateContact.Result>(ContactErrors.DuplicateEmail));
        var (provider, _) = await OpenDialogAsync(null);
        provider.Find("input[type=email]").Input("ada@example.test");

        await provider.Find("form").SubmitAsync();

        provider.WaitForAssertion(() =>
            provider.Markup.Should().Contain(ContactErrors.DuplicateEmail.Message));
        ErrorLabels(provider).Should().Equal("E-Mail");
        await _create.Received(1).Handle(
            Arg.Is<CreateContact.Command>(c => c.Email == "ada@example.test"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_OrganizationNotFound_ShowsErrorAtOrganization()
    {
        _create.Handle(Arg.Any<CreateContact.Command>(), Arg.Any<CancellationToken>())
            .Returns(Fail<CreateContact.Result>(ContactErrors.OrganizationNotFound));
        var (provider, _) = await OpenDialogAsync(null);

        await provider.Find("form").SubmitAsync();

        provider.WaitForAssertion(() => ErrorLabels(provider).Should().Equal("Organisation"));
    }

    [Fact]
    public async Task Submit_Success_ClosesDialogWithContactId()
    {
        var id = Guid.CreateVersion7();
        _create.Handle(Arg.Any<CreateContact.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateContact.Result>.Success(new CreateContact.Result(id)));
        var (provider, dialog) = await OpenDialogAsync(null);
        provider.Find("input").Input("Ada");

        await provider.Find("form").SubmitAsync();

        var result = await dialog.Result;
        result!.Canceled.Should().BeFalse();
        result.Data.Should().Be(id);
    }

    [Fact]
    public async Task Submit_EditModeWithOrganization_SendsUpdateWithOrganizationId()
    {
        var id = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        _get.Handle(new GetContact.Query(id), Arg.Any<CancellationToken>())
            .Returns(Result<GetContact.Result>.Success(new GetContact.Result(
                id, "Ada", "Lovelace", null, null, "CTO", null, organizationId, "Contoso", OrganizationType.Client, null, false)));
        _update.Handle(Arg.Any<UpdateContact.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<UpdateContact.Result>.Success(new UpdateContact.Result(id, organizationId)));
        var (provider, dialog) = await OpenDialogAsync(id);
        provider.WaitForAssertion(() => provider.Markup.Should().Contain("Contoso"));

        await provider.Find("form").SubmitAsync();

        (await dialog.Result)!.Data.Should().Be(id);
        await _update.Received(1).Handle(
            new UpdateContact.Command(id, "Ada", "Lovelace", null, null, "CTO", null, organizationId, null, null),
            Arg.Any<CancellationToken>());
    }

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> OpenDialogAsync(Guid? contactId)
    {
        var provider = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var parameters = new DialogParameters<ContactDialog> { { d => d.ContactId, contactId } };
        var dialog = await provider.InvokeAsync(() =>
            Services.GetRequiredService<IDialogService>().ShowAsync<ContactDialog>("Kontakt", parameters));
        return (provider, dialog);
    }

    private static IEnumerable<string> ErrorLabels(IRenderedComponent<MudDialogProvider> provider) =>
        provider.FindAll("label.mud-input-error").Select(l => l.TextContent.Trim());

    private static Task<Result<T>> Fail<T>(Error error) => Task.FromResult(Result<T>.Failure(error));
}
