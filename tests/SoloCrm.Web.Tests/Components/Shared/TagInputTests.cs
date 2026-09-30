using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

/// <summary>Tag chip input of the detail views (US-15 AK1).</summary>
public sealed class TagInputTests : BunitContext
{
    private static readonly Guid ContactId = Guid.CreateVersion7();
    private static readonly TagRef Remote = new(Guid.CreateVersion7(), "Remote", "#15803D");
    private static readonly TagRef Kunde = new(Guid.CreateVersion7(), "Kunde", "#1D4ED8");

    private readonly IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> _recordTags =
        Substitute.For<IQueryHandler<GetRecordTags.Query, GetRecordTags.Result>>();

    private readonly IQueryHandler<SearchTags.Query, SearchTags.Result> _search =
        Substitute.For<IQueryHandler<SearchTags.Query, SearchTags.Result>>();

    private readonly ICommandHandler<AssignTag.Command, AssignTag.Result> _assign =
        Substitute.For<ICommandHandler<AssignTag.Command, AssignTag.Result>>();

    private readonly ICommandHandler<RemoveTag.Command, RemoveTag.Result> _remove =
        Substitute.For<ICommandHandler<RemoveTag.Command, RemoveTag.Result>>();

    public TagInputTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_recordTags);
        Services.AddSingleton(_search);
        Services.AddSingleton(_assign);
        Services.AddSingleton(_remove);
        _recordTags.Handle(Arg.Any<GetRecordTags.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetRecordTags.Result>.Success(new GetRecordTags.Result([Remote])));
        _search.Handle(Arg.Any<SearchTags.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<SearchTags.Result>.Success(new SearchTags.Result([Kunde, Remote])));
        _assign.Handle(Arg.Any<AssignTag.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<AssignTag.Result>.Success(new AssignTag.Result(Guid.CreateVersion7(), "x", "#1D4ED8")));
        _remove.Handle(Arg.Any<RemoveTag.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<RemoveTag.Result>.Success(new RemoveTag.Result(Remote.Id, true)));
    }

    [Fact]
    public void Render_AssignedTags_ShowsChips()
    {
        var (input, _) = RenderInput();

        input.FindAll(".tag-chip").Select(c => c.TextContent.Trim()).Should().Equal("Remote");
    }

    [Fact]
    public async Task Type_NewName_OffersCreateAndHidesAssignedTags()
    {
        var (input, popovers) = RenderInput();

        await input.Find("input").InputAsync(new ChangeEventArgs { Value = "Stammkunde" });

        popovers.WaitForAssertion(() => popovers.Markup.Should().Contain("Neu anlegen: „Stammkunde“"));
        var suggestions = popovers.FindAll(".mud-list-item").Select(i => i.TextContent.Trim()).ToList();
        suggestions.Should().Contain("Kunde").And.NotContain("Remote");
    }

    [Fact]
    public async Task Type_ExistingNameInOtherCase_DoesNotOfferCreate()
    {
        var (input, popovers) = RenderInput();

        await input.Find("input").InputAsync(new ChangeEventArgs { Value = "kunde" });

        popovers.WaitForAssertion(() => popovers.FindAll(".mud-list-item").Should().NotBeEmpty());
        popovers.Markup.Should().NotContain("Neu anlegen");
    }

    [Fact]
    public async Task Choose_CreateOption_AssignsNewTagName()
    {
        var (input, popovers) = RenderInput();
        await input.Find("input").InputAsync(new ChangeEventArgs { Value = "  Stamm   kunde " });
        popovers.WaitForAssertion(() => popovers.Markup.Should().Contain("Neu anlegen: „Stamm kunde“"));

        await popovers.FindAll(".mud-list-item").Single(i => i.TextContent.Contains("Neu anlegen", StringComparison.Ordinal)).ClickAsync(new());

        input.WaitForAssertion(() => _assign.Received(1).Handle(
            new AssignTag.Command(TimelineRecordType.Contact, ContactId, null, "Stamm kunde"), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task RemoveChip_CallsRemoveAndReloads()
    {
        var (input, _) = RenderInput();

        await input.Find(".tag-chip .mud-chip-close-button").ClickAsync(new());

        await _remove.Received(1).Handle(new RemoveTag.Command(TimelineRecordType.Contact, ContactId, Remote.Id), Arg.Any<CancellationToken>());
        await _recordTags.Received(2).Handle(Arg.Any<GetRecordTags.Query>(), Arg.Any<CancellationToken>());
    }

    private (IRenderedComponent<TagInput> Input, IRenderedComponent<MudPopoverProvider> Popovers) RenderInput()
    {
        var popovers = Render<MudPopoverProvider>();
        var input = Render<TagInput>(p => p
            .Add(i => i.RecordType, TimelineRecordType.Contact)
            .Add(i => i.RecordId, ContactId));
        return (input, popovers);
    }
}
