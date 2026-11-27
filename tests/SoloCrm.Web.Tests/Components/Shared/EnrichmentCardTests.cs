using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Domain.Enrichment;
using SoloCrm.Web.Components.Shared;
using State = SoloCrm.Application.Features.Enrichment.GetEnrichmentSuggestions;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class EnrichmentCardTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly Guid _organizationId = Guid.CreateVersion7();
    private readonly IEnrichmentAvailability _availability = Substitute.For<IEnrichmentAvailability>();
    private readonly IQueryHandler<State.Query, State.Result> _get = Substitute.For<IQueryHandler<State.Query, State.Result>>();
    private readonly ICommandHandler<RequestEnrichment.Command, RequestEnrichment.Result> _request =
        Substitute.For<ICommandHandler<RequestEnrichment.Command, RequestEnrichment.Result>>();
    private readonly ICommandHandler<AcceptSuggestion.Command, AcceptSuggestion.Result> _accept =
        Substitute.For<ICommandHandler<AcceptSuggestion.Command, AcceptSuggestion.Result>>();
    private readonly ICommandHandler<RejectSuggestion.Command, RejectSuggestion.Result> _reject =
        Substitute.For<ICommandHandler<RejectSuggestion.Command, RejectSuggestion.Result>>();

    public EnrichmentCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_availability);
        Services.AddSingleton(_get);
        Services.AddSingleton(_request);
        Services.AddSingleton(_accept);
        Services.AddSingleton(_reject);
        _availability.IsAvailable.Returns(true);
    }

    [Fact]
    public void Render_NotConfigured_DisablesTheButtonAndExplainsWhy()
    {
        _availability.IsAvailable.Returns(false);

        var card = RenderCard();

        card.Find("[data-test=enrich-button]").HasAttribute("disabled").Should().BeTrue();
        card.Find("[data-test=enrichment-unavailable]").TextContent.Should().Contain("nicht eingerichtet");
        _get.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Render_NeverEnriched_OffersTheButton()
    {
        Returns(null, []);

        var card = RenderCard();

        card.Find("[data-test=enrich-button]").HasAttribute("disabled").Should().BeFalse();
        card.FindAll("[data-test=suggestion]").Should().BeEmpty();
    }

    [Fact]
    public void Render_ArchivedOrganization_DisablesTheButton()
    {
        Returns(null, []);

        var card = RenderCard(archived: true);

        card.Find("[data-test=enrich-button]").HasAttribute("disabled").Should().BeTrue();
        card.Markup.Should().Contain("archivierte Organisation");
    }

    [Fact]
    public async Task Click_Enrich_RequestsAndShowsTheRunningState()
    {
        var running = new State.RequestState(Guid.CreateVersion7(), EnrichmentRequestStatus.Pending, Now, null);
        _get.Handle(Arg.Any<State.Query>(), Arg.Any<CancellationToken>()).Returns(
            Result<State.Result>.Success(new State.Result(null, [])),
            Result<State.Result>.Success(new State.Result(running, [])));
        _request.Handle(new RequestEnrichment.Command(_organizationId), Arg.Any<CancellationToken>())
            .Returns(Result<RequestEnrichment.Result>.Success(new RequestEnrichment.Result(running.Id)));
        var card = RenderCard(pollInterval: TimeSpan.FromMinutes(5));

        await card.Find("[data-test=enrich-button]").ClickAsync(new());

        card.Find("[data-test=enrichment-running]").TextContent.Should().Contain("läuft");
        card.Find("[data-test=enrich-button]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task Click_EnrichRejected_ShowsTheErrorMessage()
    {
        Returns(null, []);
        _request.Handle(Arg.Any<RequestEnrichment.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<RequestEnrichment.Result>.Failure(RequestEnrichment.DailyLimitReached(20)));
        var card = RenderCard();

        await card.Find("[data-test=enrich-button]").ClickAsync(new());

        card.Markup.Should().NotContain("enrichment-running");
        await _request.Received(1).Handle(new RequestEnrichment.Command(_organizationId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Poll_RunningRequestFinishes_ShowsTheSuggestions()
    {
        var id = Guid.CreateVersion7();
        var running = new State.RequestState(id, EnrichmentRequestStatus.Running, Now, null);
        var done = new State.RequestState(id, EnrichmentRequestStatus.Completed, Now, null);
        _get.Handle(Arg.Any<State.Query>(), Arg.Any<CancellationToken>()).Returns(
            Result<State.Result>.Success(new State.Result(running, [])),
            Result<State.Result>.Success(new State.Result(done, [Item(EnrichmentField.Phone, "+49 30 1", "030 1")])));

        var card = RenderCard(pollInterval: TimeSpan.FromMilliseconds(10));

        await card.WaitForAssertionAsync(() => card.FindAll("[data-test=suggestion]").Should().HaveCount(1));
        card.FindAll("[data-test=enrichment-running]").Should().BeEmpty();
    }

    [Fact]
    public void Render_FailedRequest_ShowsTheError()
    {
        var failed = new State.RequestState(Guid.CreateVersion7(), EnrichmentRequestStatus.Failed, Now, "Agent nicht erreichbar");
        _get.Handle(Arg.Any<State.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<State.Result>.Success(new State.Result(failed, [])));

        var card = RenderCard();

        card.Find("[data-test=enrichment-failed]").TextContent.Should().Contain("fehlgeschlagen").And.Contain("Agent nicht erreichbar");
        card.Find("[data-test=enrich-button]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Render_Suggestion_ShowsCurrentProposalSourceAndConfidence()
    {
        Returns(Completed(), [Item(EnrichmentField.Phone, "+49 30 1", "030 1")]);

        var card = RenderCard();

        var suggestion = card.Find("[data-test=suggestion]");
        suggestion.QuerySelector("[data-test=current-value]")!.TextContent.Should().Be("Aktuell: 030 1");
        suggestion.QuerySelector("[data-test=proposed-value]")!.TextContent.Should().Be("Vorschlag: +49 30 1");
        suggestion.TextContent.Should().Contain("Konfidenz 80 %").And.Contain("Telefon");
        var link = suggestion.QuerySelector("[data-test=sources] a")!;
        link.GetAttribute("href").Should().Be("https://www.beispiel.test/impressum");
        link.TextContent.Should().Be("beispiel.test");
        link.GetAttribute("rel").Should().Contain("noopener");
    }

    [Fact]
    public void Render_EmptyCurrentValue_ShowsADash()
    {
        Returns(Completed(), [Item(EnrichmentField.Email, "info@beispiel.test", null)]);

        var card = RenderCard();

        card.Find("[data-test=current-value]").TextContent.Should().Be("Aktuell: –");
    }

    [Fact]
    public void Render_SourceWithOtherScheme_IsNotLinked()
    {
        Returns(Completed(), [Item(EnrichmentField.Phone, "1", null) with { SourceUrls = ["javascript:alert(1)", "https://ok.test/"] }]);

        var card = RenderCard();

        var hrefs = card.FindAll("[data-test=sources] a").Select(a => a.GetAttribute("href"));
        hrefs.Should().Equal("https://ok.test/");
    }

    [Fact]
    public void Render_UnchangedSuggestion_ShowsAHint()
    {
        Returns(Completed(), [Item(EnrichmentField.Phone, "030 1", "030 1") with { IsUnchanged = true }]);

        RenderCard().Markup.Should().Contain("entspricht dem aktuellen Wert");
    }

    [Fact]
    public void Render_CompanyProfile_ShowsRenderedMarkdownAndTheNoteHint()
    {
        Returns(Completed(), [Item(EnrichmentField.CompanyProfile, "**Branche:** Software <script>x</script>", null)]);

        var card = RenderCard();

        var preview = card.Find("[data-test=profile-preview]");
        preview.QuerySelector("strong")!.TextContent.Should().Be("Branche:");
        preview.InnerHtml.Should().NotContain("<script>");
        card.Markup.Should().Contain("Wird als Notiz in der Timeline angelegt");
        card.FindAll("[data-test=current-value]").Should().BeEmpty();
    }

    [Fact]
    public void Render_CompletedWithoutOpenSuggestions_SaysSo()
    {
        Returns(Completed(), []);

        RenderCard().Find("[data-test=no-suggestions]").TextContent.Should().Contain("Keine offenen Vorschläge");
    }

    [Fact]
    public async Task Click_Accept_AppliesRefreshesAndNotifiesThePage()
    {
        var suggestion = Item(EnrichmentField.Phone, "+49 30 1", null);
        _get.Handle(Arg.Any<State.Query>(), Arg.Any<CancellationToken>()).Returns(
            Result<State.Result>.Success(new State.Result(Completed(), [suggestion])),
            Result<State.Result>.Success(new State.Result(Completed(), [])));
        _accept.Handle(new AcceptSuggestion.Command(suggestion.Id), Arg.Any<CancellationToken>())
            .Returns(Result<AcceptSuggestion.Result>.Success(new AcceptSuggestion.Result(suggestion.Id, _organizationId, null)));
        var applied = 0;
        var card = RenderCard(onApplied: () => applied++);

        await card.Find("[data-test=accept]").ClickAsync(new());

        await _accept.Received(1).Handle(new AcceptSuggestion.Command(suggestion.Id), Arg.Any<CancellationToken>());
        applied.Should().Be(1);
        card.FindAll("[data-test=suggestion]").Should().BeEmpty();
    }

    [Fact]
    public async Task Click_AcceptFails_DoesNotNotifyThePageButReloads()
    {
        var suggestion = Item(EnrichmentField.Phone, "1", null);
        Returns(Completed(), [suggestion]);
        _accept.Handle(Arg.Any<AcceptSuggestion.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<AcceptSuggestion.Result>.Failure(EnrichmentSuggestionErrors.NotOpen(EnrichmentSuggestionStatus.Superseded)));
        var applied = 0;
        var card = RenderCard(onApplied: () => applied++);

        await card.Find("[data-test=accept]").ClickAsync(new());

        applied.Should().Be(0);
        await _get.Received(2).Handle(Arg.Any<State.Query>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Click_Reject_RejectsAndReloadsWithoutNotifyingThePage()
    {
        var suggestion = Item(EnrichmentField.Phone, "1", null);
        _get.Handle(Arg.Any<State.Query>(), Arg.Any<CancellationToken>()).Returns(
            Result<State.Result>.Success(new State.Result(Completed(), [suggestion])),
            Result<State.Result>.Success(new State.Result(Completed(), [])));
        _reject.Handle(new RejectSuggestion.Command(suggestion.Id), Arg.Any<CancellationToken>())
            .Returns(Result<RejectSuggestion.Result>.Success(new RejectSuggestion.Result(suggestion.Id)));
        var applied = 0;
        var card = RenderCard(onApplied: () => applied++);

        await card.Find("[data-test=reject]").ClickAsync(new());

        await _reject.Received(1).Handle(new RejectSuggestion.Command(suggestion.Id), Arg.Any<CancellationToken>());
        applied.Should().Be(0);
        card.FindAll("[data-test=suggestion]").Should().BeEmpty();
    }

    private IRenderedComponent<EnrichmentCard> RenderCard(bool archived = false, TimeSpan? pollInterval = null, Action? onApplied = null) =>
        Render<EnrichmentCard>(p => p
            .Add(c => c.OrganizationId, _organizationId)
            .Add(c => c.IsArchived, archived)
            .Add(c => c.PollInterval, pollInterval ?? TimeSpan.FromMinutes(5))
            .Add(c => c.OnApplied, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => onApplied?.Invoke())));

    private static State.RequestState Completed() =>
        new(Guid.CreateVersion7(), EnrichmentRequestStatus.Completed, Now, null);

    private void Returns(State.RequestState? latest, IReadOnlyList<State.SuggestionItem> suggestions) =>
        _get.Handle(new State.Query(_organizationId), Arg.Any<CancellationToken>())
            .Returns(Result<State.Result>.Success(new State.Result(latest, suggestions)));

    private static State.SuggestionItem Item(EnrichmentField field, string proposed, string? live) =>
        new(Guid.CreateVersion7(), field, EnrichmentValues.Label(field), proposed, live, ["https://www.beispiel.test/impressum"], 0.8m, false);
}
