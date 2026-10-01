using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.ApiKeys;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Web.Endpoints;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>REST API (US-17, SPEC 5) through the real host: authentication, rate limit, Problem Details, CRUD, merge patch.</summary>
[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed class ApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private CrmWebApplicationFactory _factory = null!;
    private string _key = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await CreateDatabaseAsync(postgres, Ct);
        _factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword);
        _key = (await SendAsync<CreateApiKey.Command, CreateApiKey.Result>(new CreateApiKey.Command("n8n"))).Value.Key;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Get_WithoutKey_ReturnsUnauthorizedProblem()
    {
        var response = await CreateClient(key: null).GetAsync("/api/v1/contacts", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.WwwAuthenticate.ToString().Should().Contain("X-Api-Key");
    }

    [Theory]
    [InlineData("scrm_abcdefgh_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("not-a-key")]
    public async Task Get_WrongKey_ReturnsUnauthorized(string key)
    {
        var response = await CreateClient(key).GetAsync("/api/v1/contacts", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_RevokedKey_ReturnsUnauthorized()
    {
        await using (var db = CreateDbContext(_factory.ConnectionString))
        {
            var id = await db.ApiKeys.Select(k => k.Id).SingleAsync(Ct);
            await SendAsync<RevokeApiKey.Command, RevokeApiKey.Result>(new RevokeApiKey.Command(id));
        }

        var response = await CreateClient().GetAsync("/api/v1/contacts", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_SignedInWithCookieButWithoutKey_ReturnsUnauthorized()
    {
        var client = CreateClient(key: null);
        (await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        var response = await client.GetAsync("/api/v1/contacts", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_MoreThanSixtyRequestsPerMinute_ReturnsTooManyRequestsWithRetryAfter()
    {
        var client = CreateClient();
        for (var i = 0; i < ApiRateLimiting.PermitLimit; i++)
        {
            (await client.GetAsync("/api/v1/stages", Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "request {0} is within the limit", i + 1);
        }

        var rejected = await client.GetAsync("/api/v1/stages", Ct);

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task PostContact_NewOrganizationName_CreatesContactAndReusesOrganizationIgnoringCase()
    {
        var client = CreateClient();

        var first = await client.PostAsJsonAsync("/api/v1/contacts",
            new { firstName = "Ada", lastName = "Lovelace", email = "ada@example.test", organizationName = "Contoso", source = "LinkedIn" }, Ct);
        var second = await client.PostAsJsonAsync("/api/v1/contacts",
            new { lastName = "Hopper", organizationName = " contoso " }, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var ada = await ReadAsync(first);
        first.Headers.Location!.ToString().Should().Be($"/api/v1/contacts/{ada.GetProperty("id").GetGuid()}");
        ada.GetProperty("source").GetString().Should().Be("LinkedIn");
        ada.GetProperty("organizationName").GetString().Should().Be("Contoso");
        var hopper = await ReadAsync(second);
        hopper.GetProperty("organizationId").GetGuid().Should().Be(ada.GetProperty("organizationId").GetGuid());
        await using var db = CreateDbContext(_factory.ConnectionString);
        (await db.Organizations.CountAsync(Ct)).Should().Be(1);
        (await db.AuditEntries.CountAsync(a => a.EntityType == "Contact", Ct)).Should().Be(2, "API writes are audited like the UI");
    }

    [Fact]
    public async Task PostContact_Invalid_ReturnsValidationProblemWithCamelCaseFields()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/v1/contacts", new { email = "kein-mail" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadAsync(response);
        problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain(["lastName", "email"]);
    }

    [Fact]
    public async Task PostContact_DuplicateEmail_ReturnsConflict()
    {
        var client = CreateClient();
        await client.PostAsJsonAsync("/api/v1/contacts", new { lastName = "Lovelace", email = "ada@example.test" }, Ct);

        var response = await client.PostAsJsonAsync("/api/v1/contacts", new { lastName = "Byron", email = "ADA@example.test" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("Contact.DuplicateEmail");
    }

    [Fact]
    public async Task PatchContact_MergePatch_KeepsMissingFieldsAndClearsNullFields()
    {
        var client = CreateClient();
        var created = await ReadAsync(await client.PostAsJsonAsync("/api/v1/contacts",
            new { firstName = "Ada", lastName = "Lovelace", phone = "+49 30 123", jobTitle = "CTO" }, Ct));
        var id = created.GetProperty("id").GetGuid();

        var response = await PatchAsync(client, $"/api/v1/contacts/{id}", """{ "jobTitle": "CEO", "phone": null }""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var patched = await ReadAsync(response);
        patched.GetProperty("firstName").GetString().Should().Be("Ada");
        patched.GetProperty("jobTitle").GetString().Should().Be("CEO");
        patched.GetProperty("phone").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PatchContact_UnknownFieldOrWrongType_ReturnsValidationProblem()
    {
        var client = CreateClient();
        var id = (await ReadAsync(await client.PostAsJsonAsync("/api/v1/contacts", new { lastName = "Lovelace" }, Ct))).GetProperty("id").GetGuid();

        var response = await PatchAsync(client, $"/api/v1/contacts/{id}", """{ "nickname": "Ada", "organizationId": 42 }""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("errors").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["nickname", "organizationId"]);
    }

    [Fact]
    public async Task Address_PostAndMergePatch_UsesFlatFieldsAndReportsCountryErrors()
    {
        var client = CreateClient();
        var created = await ReadAsync(await client.PostAsJsonAsync("/api/v1/organizations",
            new { name = "Contoso", street = "Hauptstr. 1", postalCode = "10115", city = "Berlin", countryCode = "DE" }, Ct));
        var id = created.GetProperty("id").GetGuid();

        var patched = await ReadAsync(await PatchAsync(client, $"/api/v1/organizations/{id}", """{ "city": "Potsdam", "postalCode": "14467" }"""));
        var invalid = await PatchAsync(client, $"/api/v1/organizations/{id}", """{ "countryCode": "Deutschland" }""");

        patched.GetProperty("street").GetString().Should().Be("Hauptstr. 1", "fields that are not sent stay unchanged");
        patched.GetProperty("city").GetString().Should().Be("Potsdam");
        patched.GetProperty("countryCode").GetString().Should().Be("DE");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(invalid)).GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Equal("countryCode");
    }

    [Fact]
    public async Task GetContact_UnknownId_ReturnsNotFoundProblem()
    {
        var response = await CreateClient().GetAsync($"/api/v1/contacts/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetContacts_SearchTagAndPaging_UsesSameSearchAsUi()
    {
        var client = CreateClient();
        var schmidt = (await ReadAsync(await client.PostAsJsonAsync("/api/v1/contacts", new { firstName = "Anna", lastName = "Schmidt" }, Ct)))
            .GetProperty("id").GetGuid();
        await client.PostAsJsonAsync("/api/v1/contacts", new { firstName = "Bernd", lastName = "Meyer" }, Ct);
        await SendAsync<AssignTag.Command, AssignTag.Result>(new AssignTag.Command(TimelineRecordType.Contact, schmidt, null, "Kunde"));

        var typo = await ReadAsync(await client.GetAsync("/api/v1/contacts?search=Schmitt", Ct));
        var tagged = await ReadAsync(await client.GetAsync("/api/v1/contacts?tag=kunde", Ct));
        var unknownTag = await ReadAsync(await client.GetAsync("/api/v1/contacts?tag=gibtsnicht", Ct));
        var paged = await ReadAsync(await client.GetAsync("/api/v1/contacts?page=2&pageSize=1", Ct));
        var invalid = await client.GetAsync("/api/v1/contacts?page=0&pageSize=500", Ct);

        typo.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("lastName").GetString()).Should().Equal("Schmidt");
        tagged.GetProperty("items").EnumerateArray().Single().GetProperty("tags")[0].GetProperty("name").GetString().Should().Be("Kunde");
        unknownTag.GetProperty("totalCount").GetInt32().Should().Be(0);
        paged.GetProperty("items").GetArrayLength().Should().Be(1);
        paged.GetProperty("page").GetInt32().Should().Be(2);
        paged.GetProperty("totalCount").GetInt32().Should().Be(2);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(invalid)).GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["page", "pageSize"]);
    }

    [Fact]
    public async Task PatchOpportunity_StageId_ChangesStageAndRaisesEvent()
    {
        var client = CreateClient();
        var stages = (await ReadAsync(await client.GetAsync("/api/v1/stages", Ct))).EnumerateArray().ToList();
        var won = stages.Single(s => s.GetProperty("status").GetString() == "Won").GetProperty("id").GetGuid();
        var lost = stages.Single(s => s.GetProperty("status").GetString() == "Lost").GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync("/api/v1/opportunities",
            new { title = ".NET-Architekt", pricingModel = "Daily", amount = 760, durationValue = 3, durationUnit = "Months" }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await ReadAsync(created)).GetProperty("id").GetGuid();

        var withoutReason = await PatchAsync(client, $"/api/v1/opportunities/{id}", $$"""{ "stageId": "{{lost}}" }""");
        var toWon = await PatchAsync(client, $"/api/v1/opportunities/{id}", $$"""{ "stageId": "{{won}}" }""");

        withoutReason.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(withoutReason)).GetProperty("code").GetString().Should().Be("Opportunity.LostReasonRequired");
        toWon.StatusCode.Should().Be(HttpStatusCode.OK);
        var opportunity = await ReadAsync(toWon);
        opportunity.GetProperty("stageStatus").GetString().Should().Be("Won");
        opportunity.GetProperty("amount").GetDecimal().Should().Be(760m, "fields that are not sent stay unchanged");
        opportunity.GetProperty("estimatedValue").GetDecimal().Should().Be(760m * 60);
        await using var db = CreateDbContext(_factory.ConnectionString);
        (await db.OutboxMessages.CountAsync(m => m.Type == "opportunity.stage_changed", Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Tasks_CreateCompleteAndReopen_ViaPatch()
    {
        var client = CreateClient();
        var created = await client.PostAsJsonAsync("/api/v1/tasks", new { title = "Angebot nachfassen", dueDate = "2026-10-05" }, Ct);
        var id = (await ReadAsync(created)).GetProperty("id").GetGuid();

        var completed = await ReadAsync(await PatchAsync(client, $"/api/v1/tasks/{id}", """{ "completed": true, "title": "Angebot telefonisch nachfassen" }"""));
        var reopened = await ReadAsync(await PatchAsync(client, $"/api/v1/tasks/{id}", """{ "completed": false }"""));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        completed.GetProperty("completed").GetBoolean().Should().BeTrue();
        completed.GetProperty("title").GetString().Should().Be("Angebot telefonisch nachfassen");
        completed.GetProperty("dueDate").GetString().Should().Be("2026-10-05");
        reopened.GetProperty("completed").GetBoolean().Should().BeFalse();
        reopened.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PostActivity_WithAndWithoutLink_CreatesOrRejects()
    {
        var client = CreateClient();
        var contact = (await ReadAsync(await client.PostAsJsonAsync("/api/v1/contacts", new { lastName = "Lovelace" }, Ct))).GetProperty("id").GetGuid();

        var created = await client.PostAsJsonAsync("/api/v1/activities", new { type = "Call", body = "Kurz telefoniert", contactId = contact }, Ct);
        var unlinked = await client.PostAsJsonAsync("/api/v1/activities", new { body = "Ohne Bezug" }, Ct);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        unlinked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownApiPath_ReturnsNotFoundProblem()
    {
        var response = await CreateClient().GetAsync("/api/v1/unknown", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task ApiDocs_Anonymous_RedirectToLogin(string path)
    {
        var response = await CreateClient(key: null).GetAsync(path, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/Account/Login");
    }

    [Fact]
    public async Task ApiDocs_SignedIn_ServesOpenApiDocumentAndScalar()
    {
        var client = CreateClient(key: null);
        await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);

        var document = await ReadAsync(await client.GetAsync("/openapi/v1.json", Ct));
        var scalar = await client.GetAsync("/scalar/v1", Ct);

        document.GetProperty("info").GetProperty("title").GetString().Should().Be("SoloCRM API");
        document.GetProperty("paths").EnumerateObject().Select(p => p.Name)
            .Should().Contain(["/api/v1/contacts", "/api/v1/contacts/{id}", "/api/v1/opportunities/{id}", "/api/v1/stages"])
            .And.NotContain(p => p.StartsWith("/Account", StringComparison.Ordinal));
        document.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey").GetProperty("name").GetString()
            .Should().Be("X-Api-Key");
        scalar.StatusCode.Should().Be(HttpStatusCode.OK);
        var configuration = await scalar.Content.ReadAsStringAsync(Ct);
        configuration.Should().Contain("\"telemetry\":false").And.Contain("\"withDefaultFonts\":false");
    }

    private HttpClient CreateClient(string? key = "")
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyAuthentication.HeaderName, key.Length == 0 ? _key : key);
        }

        return client;
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string path, string json)
    {
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/merge-patch+json");
        return client.PatchAsync(path, content, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<Result<TResult>> SendAsync<TCommand, TResult>(TCommand command)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>().Handle(command, Ct);
    }
}
