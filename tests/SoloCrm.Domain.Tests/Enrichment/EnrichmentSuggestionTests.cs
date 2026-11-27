using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Domain.Tests.Enrichment;

public sealed class EnrichmentSuggestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.FromHours(1));

    private static EnrichmentSuggestion Create(
        string value = " +49 30 1234567 ", string? current = null, string[]? urls = null, decimal confidence = 0.85m) =>
        EnrichmentSuggestion.Create(
            Guid.CreateVersion7(), EnrichmentField.Phone, value, current, urls ?? ["https://example.test/impressum"], confidence);

    [Fact]
    public void Create_ValidValues_IsOpenAndTrimmed()
    {
        var suggestion = Create(current: "  ");

        suggestion.Status.Should().Be(EnrichmentSuggestionStatus.Open);
        suggestion.IsOpen.Should().BeTrue();
        suggestion.ProposedValue.Should().Be("+49 30 1234567");
        suggestion.CurrentValue.Should().BeNull();
        suggestion.SourceUrls.Should().Equal("https://example.test/impressum");
        suggestion.Confidence.Should().Be(0.85m);
        suggestion.DecidedAt.Should().BeNull();
    }

    [Fact]
    public void Create_CurrentValue_IsKept()
    {
        Create(current: " 030 111 ").CurrentValue.Should().Be("030 111");
    }

    [Fact]
    public void Create_CompanyProfileWithSeveralSources_KeepsDistinctSources()
    {
        var suggestion = EnrichmentSuggestion.Create(
            Guid.CreateVersion7(),
            EnrichmentField.CompanyProfile,
            "**Branche:** Software",
            null,
            ["https://a.test/", "https://b.test/about", "https://a.test/"],
            0.6m);

        suggestion.SourceUrls.Should().Equal("https://a.test/", "https://b.test/about");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankValue_Throws(string value)
    {
        var act = () => Create(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_TooLongValue_Throws()
    {
        var act = () => Create(new string('a', EnrichmentSuggestion.ValueMaxLength + 1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_ConfidenceOutOfRange_Throws(double confidence)
    {
        var act = () => Create(confidence: (decimal)confidence);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Create_ConfidenceAtTheBounds_IsAccepted(double confidence)
    {
        Create(confidence: (decimal)confidence).Confidence.Should().Be((decimal)confidence);
    }

    [Fact]
    public void Create_NoSource_Throws()
    {
        var act = () => Create(urls: []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_TooManySources_Throws()
    {
        var urls = Enumerable.Range(0, EnrichmentSuggestion.MaxSourceUrls + 1).Select(i => $"https://example.test/{i}").ToArray();

        var act = () => Create(urls: urls);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("example.test/impressum")]
    [InlineData("ftp://example.test/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/impressum")]
    [InlineData("")]
    public void Create_SourceThatIsNotAnAbsoluteWebUrl_Throws(string url)
    {
        var act = () => Create(urls: [url]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_EmptyRequest_Throws()
    {
        var act = () => EnrichmentSuggestion.Create(Guid.Empty, EnrichmentField.Email, "a@b.test", null, ["https://b.test/"], 0.5m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Accept_OpenSuggestion_IsAcceptedWithTime()
    {
        var suggestion = Create();

        suggestion.Accept(Now);

        suggestion.Status.Should().Be(EnrichmentSuggestionStatus.Accepted);
        suggestion.DecidedAt.Should().Be(Now);
        suggestion.DecidedAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        suggestion.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Reject_OpenSuggestion_IsRejectedWithTime()
    {
        var suggestion = Create();

        suggestion.Reject(Now);

        suggestion.Status.Should().Be(EnrichmentSuggestionStatus.Rejected);
        suggestion.DecidedAt.Should().Be(Now);
    }

    [Fact]
    public void Supersede_OpenSuggestion_IsSupersededWithoutDecision()
    {
        var suggestion = Create();

        suggestion.Supersede();

        suggestion.Status.Should().Be(EnrichmentSuggestionStatus.Superseded);
        suggestion.DecidedAt.Should().BeNull();
    }

    [Fact]
    public void Decide_NotOpenSuggestion_Throws()
    {
        var accepted = Create();
        accepted.Accept(Now);
        var rejected = Create();
        rejected.Reject(Now);
        var superseded = Create();
        superseded.Supersede();

        foreach (var suggestion in new[] { accepted, rejected, superseded })
        {
            suggestion.Invoking(s => s.Accept(Now)).Should().Throw<InvalidOperationException>();
            suggestion.Invoking(s => s.Reject(Now)).Should().Throw<InvalidOperationException>();
            suggestion.Invoking(s => s.Supersede()).Should().Throw<InvalidOperationException>();
        }
    }
}
