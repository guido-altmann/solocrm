using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;
using SoloCrm.Web.Components.Shared;
using SearchFeature = SoloCrm.Application.Features.Search.Search;

namespace SoloCrm.Web.Tests.Components.Shared;

/// <summary>Command palette (US-13): grouped hits, actions and keyboard navigation.</summary>
public sealed class CommandPaletteTests : BunitContext
{
    private static readonly Guid ContactId = Guid.CreateVersion7();

    private readonly IQueryHandler<SearchFeature.Query, SearchFeature.Result> _search =
        Substitute.For<IQueryHandler<SearchFeature.Query, SearchFeature.Result>>();

    public CommandPaletteTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_search);
        _search.Handle(Arg.Any<SearchFeature.Query>(), Arg.Any<CancellationToken>()).Returns(Success(new SearchFeature.Result(
            [new SearchFeature.ContactHit(ContactId, "Ada Lovelace", "ada@example.test", "Contoso")],
            [new SearchFeature.OrganizationHit(Guid.CreateVersion7(), "Contoso", OrganizationType.Client)],
            [new SearchFeature.OpportunityHit(Guid.CreateVersion7(), "Azure-Migration", "Angebot", StageStatus.Open, "Contoso")])));
    }

    [Fact]
    public async Task Open_WithoutText_ShowsAllActionsWithFirstSelected()
    {
        var (provider, _) = await OpenAsync();

        Options(provider).Should().Equal(PaletteItem.Actions.Select(a => a.Label));
        Selected(provider).Should().Be("Neuer Kontakt");
    }

    [Fact]
    public async Task Input_TwoCharacters_ShowsHitsGroupedByTypeWithDetails()
    {
        var (provider, _) = await OpenAsync();

        await Input(provider).InputAsync(new ChangeEventArgs { Value = "co" });

        provider.WaitForAssertion(() => Options(provider).Should().Equal("Ada Lovelace", "Contoso", "Azure-Migration"));
        provider.FindAll(".command-palette-group").Select(g => g.TextContent).Should().Equal("Kontakte", "Organisationen", "Anfragen");
        provider.Markup.Should().Contain("Endkunde").And.Contain("Angebot · Contoso");
        await _search.Received(1).Handle(new SearchFeature.Query("co"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Input_OneCharacter_FiltersActionsWithoutSearching()
    {
        var (provider, _) = await OpenAsync();

        await Input(provider).InputAsync(new ChangeEventArgs { Value = "p" });

        Options(provider).Should().Equal("Gehe zu Pipeline");
        await _search.DidNotReceiveWithAnyArgs().Handle(default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Input_MatchingActionWords_ListsActionBeforeHits()
    {
        var (provider, _) = await OpenAsync();

        await Input(provider).InputAsync(new ChangeEventArgs { Value = "neu kon" });

        provider.WaitForAssertion(() => Options(provider).First().Should().Be("Neuer Kontakt"));
        Options(provider).Should().NotContain("Neue Organisation");
    }

    [Fact]
    public async Task Input_FastTyping_CancelsAndIgnoresSupersededSearch()
    {
        var stale = new TaskCompletionSource<Result<SearchFeature.Result>>();
        CancellationToken staleToken = default;
        _search.Handle(new SearchFeature.Query("ad"), Arg.Do<CancellationToken>(t => staleToken = t)).Returns(stale.Task);
        var (provider, _) = await OpenAsync();

        var first = Input(provider).InputAsync(new ChangeEventArgs { Value = "ad" });
        await Input(provider).InputAsync(new ChangeEventArgs { Value = "ada" });
        stale.SetResult(Result<SearchFeature.Result>.Success(new SearchFeature.Result(
            [new SearchFeature.ContactHit(Guid.CreateVersion7(), "Stale Hit", null, null)], [], [])));
        await first;

        staleToken.IsCancellationRequested.Should().BeTrue();
        provider.WaitForAssertion(() => Options(provider).Should().Contain("Ada Lovelace").And.NotContain("Stale Hit"));
    }

    [Fact]
    public async Task ArrowDownAndEnter_ClosesWithSelectedEntry()
    {
        var (provider, dialog) = await OpenAsync();

        await Input(provider).KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        Selected(provider).Should().Be("Neue Organisation");
        await Input(provider).KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        var result = await dialog.Result;
        result!.Data.Should().Be(PaletteItem.Actions[1]);
    }

    [Fact]
    public async Task ArrowUp_OnFirstEntry_WrapsToLast()
    {
        var (provider, _) = await OpenAsync();

        await Input(provider).KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });

        Selected(provider).Should().Be("Gehe zu Einstellungen");
        Input(provider).GetAttribute("aria-activedescendant").Should().Be($"command-palette-item-{PaletteItem.Actions.Count - 1}");
    }

    [Fact]
    public async Task Escape_ClosesWithoutEntry()
    {
        var (provider, dialog) = await OpenAsync();

        await Input(provider).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        (await dialog.Result)!.Canceled.Should().BeTrue();
    }

    [Fact]
    public async Task Click_Hit_ClosesWithRecordLink()
    {
        var (provider, dialog) = await OpenAsync();
        await Input(provider).InputAsync(new ChangeEventArgs { Value = "ada" });
        provider.WaitForAssertion(() => Options(provider).Should().Contain("Ada Lovelace"));

        await provider.FindAll("[role=option]").First(o => o.TextContent.Contains("Ada Lovelace", StringComparison.Ordinal)).ClickAsync(new MouseEventArgs());

        (await dialog.Result)!.Data.Should().BeOfType<PaletteItem>().Which.Href.Should().Be(Links.Contact(ContactId));
    }

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> OpenAsync()
    {
        var provider = Render<MudDialogProvider>();
        var parameters = new DialogParameters<CommandPalette> { { p => p.Debounce, TimeSpan.Zero } };
        var dialog = await provider.InvokeAsync(() =>
            Services.GetRequiredService<IDialogService>().ShowAsync<CommandPalette>("Suchen", parameters));
        return (provider, dialog);
    }

    private static AngleSharp.Dom.IElement Input(IRenderedComponent<MudDialogProvider> provider) => provider.Find("input[role=combobox]");

    private static IEnumerable<string> Options(IRenderedComponent<MudDialogProvider> provider) =>
        provider.FindAll("[role=option] .command-palette-label").Select(o => o.TextContent);

    private static string Selected(IRenderedComponent<MudDialogProvider> provider) =>
        provider.Find("[role=option][aria-selected=true] .command-palette-label").TextContent;

    private static Task<Result<SearchFeature.Result>> Success(SearchFeature.Result result) =>
        Task.FromResult(Result<SearchFeature.Result>.Success(result));
}
