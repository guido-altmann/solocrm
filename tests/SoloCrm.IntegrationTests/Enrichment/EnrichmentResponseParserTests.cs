using System.Text.Json.Nodes;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Domain.Enrichment;
using SoloCrm.Infrastructure.Enrichment;
using static SoloCrm.IntegrationTests.Enrichment.AnswerJson;

namespace SoloCrm.IntegrationTests.Enrichment;

public sealed class EnrichmentResponseParserTests
{
    private static ParsedAnswer Parse(JsonNode answer) => EnrichmentResponseParser.Parse(answer.ToJsonString());

    [Fact]
    public void Parse_AllFieldsNull_ReturnsNothing()
    {
        var parsed = Parse(Answer());

        parsed.Suggestions.Should().BeEmpty();
        parsed.Discarded.Should().Be(0);
    }

    [Fact]
    public void Parse_ValidTextAndAddress_ReturnsValuesWithSourcesAndConfidence()
    {
        var parsed = Parse(Answer(
            ("phone", Field(" +49 30 1234567 ", 0.84m, Imprint)),
            ("address", Field(Address(), 0.9m, Imprint)),
            ("companyProfile", Field("**Branche:** Software", 0.5m, "https://a.test/", "https://b.test/"))));

        parsed.Suggestions.Select(s => s.Field).Should().Equal(EnrichmentField.Address, EnrichmentField.Phone, EnrichmentField.CompanyProfile);
        var phone = parsed.Suggestions.Single(s => s.Field == EnrichmentField.Phone);
        phone.Value.Should().Be("+49 30 1234567");
        phone.Confidence.Should().Be(0.84m);
        phone.SourceUrls.Should().Equal(Imprint);
        var address = EnrichmentAddress.TryParse(parsed.Suggestions.Single(s => s.Field == EnrichmentField.Address).Value);
        address.Should().Be(new EnrichmentAddress("Musterstraße 1", null, "10115", "Berlin", null, "DE"));
        parsed.Suggestions.Single(s => s.Field == EnrichmentField.CompanyProfile).SourceUrls.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_AnswerInACodeFence_IsAccepted()
    {
        var text = "```json\n" + Answer(("email", Field("info@example.test", 1m, Imprint))).ToJsonString() + "\n```";

        var parsed = EnrichmentResponseParser.Parse(text);

        parsed.Suggestions.Should().ContainSingle().Which.Value.Should().Be("info@example.test");
    }

    [Fact]
    public void Parse_ConfidenceWithManyDecimals_IsRoundedToTwo()
    {
        var parsed = Parse(Answer(("phone", Field("1", 0.8449m, Imprint))));

        parsed.Suggestions.Single().Confidence.Should().Be(0.84m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein JSON")]
    [InlineData("{\"address\": null, \"phone\":")] // cut off
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void Parse_NotAJsonObject_Throws(string text)
    {
        var act = () => EnrichmentResponseParser.Parse(text);

        act.Should().Throw<EnrichmentProviderException>().Which.IsTransient.Should().BeFalse();
    }

    [Fact]
    public void Parse_MissingProperty_Throws()
    {
        var answer = Answer();
        answer.Remove("vatId");

        var act = () => Parse(answer);

        act.Should().Throw<EnrichmentProviderException>().WithMessage("*Schema*");
    }

    [Fact]
    public void Parse_UnknownProperty_Throws()
    {
        var answer = Answer();
        answer["fax"] = null;

        var act = () => Parse(answer);

        act.Should().Throw<EnrichmentProviderException>();
    }

    [Fact]
    public void Parse_FieldIsNeitherNullNorObject_Throws()
    {
        var act = () => Parse(Answer(("phone", "+49 30 1")));

        act.Should().Throw<EnrichmentProviderException>().WithMessage("*phone*");
    }

    [Fact]
    public void Parse_FieldWithMissingOrExtraProperty_Throws()
    {
        var missing = Field("1", 0.5m, Imprint);
        missing.Remove("confidence");
        var extra = Field("1", 0.5m, Imprint);
        extra["note"] = "x";

        ((Action)(() => Parse(Answer(("phone", missing))))).Should().Throw<EnrichmentProviderException>();
        ((Action)(() => Parse(Answer(("phone", extra))))).Should().Throw<EnrichmentProviderException>();
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.01")]
    [InlineData("\"hoch\"")]
    [InlineData("null")]
    public void Parse_ConfidenceInvalid_Throws(string confidence)
    {
        var field = Field("1", 0.5m, Imprint);
        field["confidence"] = JsonNode.Parse(confidence);

        var act = () => Parse(Answer(("phone", field)));

        act.Should().Throw<EnrichmentProviderException>().WithMessage("*confidence*");
    }

    [Fact]
    public void Parse_ValueIsNotAText_Throws()
    {
        var act = () => Parse(Answer(("phone", Field(42, 0.5m, Imprint))));

        act.Should().Throw<EnrichmentProviderException>().WithMessage("*phone.value*");
    }

    [Fact]
    public void Parse_SourceUrlsIsNotAListOfTexts_Throws()
    {
        var notAList = Field("1", 0.5m, Imprint);
        notAList["sourceUrls"] = Imprint;
        var numberInList = Field("1", 0.5m, Imprint);
        numberInList["sourceUrls"] = new JsonArray(1);

        ((Action)(() => Parse(Answer(("phone", notAList))))).Should().Throw<EnrichmentProviderException>();
        ((Action)(() => Parse(Answer(("phone", numberInList))))).Should().Throw<EnrichmentProviderException>();
    }

    [Fact]
    public void Parse_AddressWithUnknownPartOrWrongType_Throws()
    {
        var unknown = Address();
        unknown["country"] = "Deutschland";
        var wrongType = Address();
        wrongType["city"] = 5;

        ((Action)(() => Parse(Answer(("address", Field(unknown, 0.5m, Imprint)))))).Should().Throw<EnrichmentProviderException>();
        ((Action)(() => Parse(Answer(("address", Field(wrongType, 0.5m, Imprint)))))).Should().Throw<EnrichmentProviderException>();
        ((Action)(() => Parse(Answer(("address", Field("Musterstraße 1, Berlin", 0.5m, Imprint)))))).Should().Throw<EnrichmentProviderException>();
    }

    [Fact]
    public void Parse_BlankValueOrNoSource_IsDiscardedWithoutFailing()
    {
        var parsed = Parse(Answer(
            ("phone", Field("   ", 0.5m, Imprint)),
            ("email", Field("info@example.test", 0.5m)),
            ("vatId", Field("DE123456789", 0.9m, Imprint))));

        parsed.Suggestions.Should().ContainSingle().Which.Field.Should().Be(EnrichmentField.VatId);
        parsed.Discarded.Should().Be(2);
    }

    [Fact]
    public void Parse_TooLongValue_IsDiscarded()
    {
        var parsed = Parse(Answer(("companyProfile", Field(new string('a', EnrichmentSuggestion.ValueMaxLength + 1), 0.5m, Imprint))));

        parsed.Suggestions.Should().BeEmpty();
        parsed.Discarded.Should().Be(1);
    }

    [Theory]
    [InlineData("Deutschland")]
    [InlineData("XX")]
    public void Parse_AddressWithUnknownCountryCode_IsDiscarded(string countryCode)
    {
        var parsed = Parse(Answer(("address", Field(Address(countryCode: countryCode), 0.5m, Imprint))));

        parsed.Suggestions.Should().BeEmpty();
        parsed.Discarded.Should().Be(1);
    }

    [Fact]
    public void Parse_EmptyAddress_IsDiscarded()
    {
        var parsed = Parse(Answer(("address", Field(Address(null, null, null, null), 0.5m, Imprint))));

        parsed.Suggestions.Should().BeEmpty();
        parsed.Discarded.Should().Be(1);
    }

    [Fact]
    public void Parse_ErrorMessage_NamesNoValuesOrUrls()
    {
        var act = () => Parse(Answer(("phone", Field(42, 0.5m, "https://geheim.test/pfad"))));

        act.Should().Throw<EnrichmentProviderException>().Which.Message.Should().NotContain("geheim").And.NotContain("42");
    }
}
