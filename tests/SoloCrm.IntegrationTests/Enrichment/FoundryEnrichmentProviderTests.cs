using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Enrichment;
using SoloCrm.Infrastructure.Enrichment;
using static SoloCrm.IntegrationTests.Enrichment.AnswerJson;

namespace SoloCrm.IntegrationTests.Enrichment;

public sealed class FoundryEnrichmentProviderTests
{
    private static readonly EnrichmentSubject Subject = new(Guid.CreateVersion7(), "Beispiel GmbH", "beispiel.test");

    private readonly FakeAgent _agent = new();
    private readonly ListLogger _logger = new();

    private FoundryEnrichmentProvider CreateProvider() => new(_agent, new FakeTimeProvider(), _logger);

    private static AgentReply Reply(JsonNode answer, params string[] cited) =>
        new(answer.ToJsonString(), cited, 3, 1200, 340, true);

    [Fact]
    public async Task EnrichAsync_ValidAnswerWithConfirmedSources_ReturnsTheValues()
    {
        _agent.Reply = Reply(
            Answer(
                ("phone", Field("+49 30 1234567", 0.84m, Imprint)),
                ("address", Field(Address(), 0.9m, Imprint))),
            Imprint);

        var values = await CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        values.Should().HaveCount(2);
        values.Single(v => v.Field == EnrichmentField.Phone).Should().BeEquivalentTo(
            new ProposedValue(EnrichmentField.Phone, "+49 30 1234567", [Imprint], 0.84m));
    }

    [Fact]
    public async Task EnrichAsync_NoCitationsFromTheService_KeepsTheValuesWithTheirNamedSources()
    {
        // The service only delivers url_citation annotations for free text, not for a structured answer.
        _agent.Reply = Reply(Answer(("phone", Field("+49 30 1", 0.9m, Imprint))));

        var values = await CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        values.Should().ContainSingle().Which.SourceUrls.Should().Equal(Imprint);
    }

    [Fact]
    public async Task EnrichAsync_SourceDiffersFromTheCitations_IsStillKept()
    {
        _agent.Reply = Reply(
            Answer(("phone", Field("+49 30 1", 0.9m, "https://andere-seite.test/kontakt"))),
            Imprint);

        var values = await CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        values.Should().ContainSingle().Which.SourceUrls.Should().Equal("https://andere-seite.test/kontakt");
    }

    [Fact]
    public async Task EnrichAsync_SeveralSources_KeepsAllWebSourcesOnce()
    {
        _agent.Reply = Reply(
            Answer(("companyProfile", Field("**Branche:** Software", 0.6m, "https://a.test/", "https://a.test/", "https://b.test/x"))));

        var values = await CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        values.Single().SourceUrls.Should().Equal("https://a.test/", "https://b.test/x");
    }

    [Fact]
    public async Task EnrichAsync_ValueWithoutAnyWebSource_IsDropped()
    {
        _agent.Reply = Reply(Answer(
            ("phone", Field("+49 30 1", 0.9m, "ftp://files.test/kontakt.txt")),
            ("email", Field("info@example.test", 0.9m, Imprint))));

        var values = await CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        values.Should().ContainSingle().Which.Field.Should().Be(EnrichmentField.Email);
    }

    [Fact]
    public async Task EnrichAsync_SchemaViolation_ThrowsNonTransient()
    {
        var answer = Answer();
        answer.Remove("phone");
        _agent.Reply = Reply(answer, Imprint);

        var act = () => CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<EnrichmentProviderException>()).Which.IsTransient.Should().BeFalse();
    }

    [Fact]
    public async Task EnrichAsync_IncompleteAnswer_ThrowsTransient()
    {
        _agent.Reply = new AgentReply("{\"address\":", [], 1, 10, 5, IsComplete: false);

        var act = () => CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<EnrichmentProviderException>()).Which.IsTransient.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task EnrichAsync_NoText_ThrowsTransient(string? text)
    {
        _agent.Reply = new AgentReply(text, [], 0, 10, 0, IsComplete: true);

        var act = () => CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<EnrichmentProviderException>()).Which.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task EnrichAsync_AgentFails_PassesTheErrorOn()
    {
        _agent.Failure = new EnrichmentProviderException("Der Agent ist nicht erreichbar.", isTransient: true);

        var act = () => CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<EnrichmentProviderException>()).Which.Message.Should().Be("Der Agent ist nicht erreichbar.");
    }

    [Fact]
    public async Task EnrichAsync_Input_ContainsOnlyNameDomainAndTheSchema()
    {
        _agent.Reply = Reply(Answer());

        await CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);

        _agent.Inputs.Should().ContainSingle().Which.Should()
            .Contain("Firma: Beispiel GmbH").And.Contain("Domain: beispiel.test")
            .And.Contain("\"additionalProperties\": false").And.Contain("companyProfile");
    }

    [Fact]
    public async Task EnrichAsync_NoDomain_LeavesTheDomainOut()
    {
        _agent.Reply = Reply(Answer());

        await CreateProvider().EnrichAsync(Subject with { Domain = null }, TestContext.Current.CancellationToken);

        _agent.Inputs.Single().Should().NotContain("Domain:");
    }

    [Fact]
    public async Task EnrichAsync_Always_LogsNoNamesValuesOrUrls()
    {
        _agent.Reply = Reply(
            Answer(
                ("phone", Field("+49 30 GEHEIM", 0.9m, Imprint)),
                ("email", Field("x@example.test", 0.9m, "https://unzitiert.test/"))),
            Imprint);

        await CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);
        _agent.Reply = new AgentReply("kaputt", [], 1, 1, 1, IsComplete: true);
        var act = () => CreateProvider().EnrichAsync(Subject, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<EnrichmentProviderException>();

        _logger.Messages.Should().HaveCount(2);
        foreach (var message in _logger.Messages)
        {
            message.Should().NotContain("Beispiel");
            message.Should().NotContain("beispiel.test");
            message.Should().NotContain("GEHEIM");
            message.Should().NotContain("example.test");
            message.Should().NotContain("unzitiert");
            message.Should().Contain(Subject.TargetId.ToString());
        }

        _logger.Messages[0].Should().Contain("2 suggestions").And.Contain("0 without source").And.Contain("1 confirmed by citations").And.Contain("3 searches").And.Contain("1200/340 tokens");
    }

    [Fact]
    public async Task NotConfiguredProvider_Enrich_ThrowsNonTransient()
    {
        var provider = new NotConfiguredEnrichmentProvider();

        var act = () => provider.EnrichAsync(Subject, TestContext.Current.CancellationToken);

        var exception = await act.Should().ThrowAsync<EnrichmentProviderException>();
        exception.Which.IsTransient.Should().BeFalse();
        provider.Name.Should().Be(FoundryEnrichmentProvider.ProviderName);
    }

    private sealed class FakeAgent : IFoundryAgent
    {
        public AgentReply Reply { get; set; } = new("{}", [], 0, null, null, true);

        public Exception? Failure { get; set; }

        public List<string> Inputs { get; } = [];

        public Task<AgentReply> AskAsync(string input, CancellationToken cancellationToken)
        {
            Inputs.Add(input);
            return Failure is null ? Task.FromResult(Reply) : Task.FromException<AgentReply>(Failure);
        }
    }

    private sealed class ListLogger : ILogger<FoundryEnrichmentProvider>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
