using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Webhooks;
using SoloCrm.Domain.Webhooks;

namespace SoloCrm.IntegrationTests.Features.Webhooks;

/// <summary>Webhook management (US-18 AK1), delivery log (AK3) and „Test senden“.</summary>
public sealed class WebhookHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private WebhookReceiver? _receiver;

    protected override IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?> { ["Webhooks:AllowedHttpHosts"] = "127.0.0.1, n8n" };

    private static readonly string[] StageChanged = ["opportunity.stage_changed"];

    [Fact]
    public async Task Create_ValidCommand_ReturnsSecretOnceAndStoresItEncrypted()
    {
        var result = await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(
            new CreateWebhook.Command(" n8n ", "https://n8n.example.test/webhook/abc", StageChanged));

        result.IsSuccess.Should().BeTrue();
        result.Value.Secret.Should().MatchRegex("^[0-9a-f]{64}$");
        await using var db = OpenDb();
        var stored = await db.WebhookSubscriptions.SingleAsync(Ct);
        stored.Name.Should().Be("n8n");
        stored.Events.Should().Equal(StageChanged);
        stored.IsActive.Should().BeTrue();
        stored.CreatedAt.Should().Be(Start);
        stored.ProtectedSecret.Should().NotContain(result.Value.Secret);
        Get<ISecretProtector>().Unprotect(stored.ProtectedSecret).Should().Be(result.Value.Secret);
    }

    [Theory]
    [InlineData("http://n8n.example.test/webhook", "Url")]
    [InlineData("ftp://n8n.example.test/webhook", "Url")]
    [InlineData("https://user:pass@n8n.example.test/webhook", "Url")]
    [InlineData("n8n.example.test/webhook", "Url")]
    [InlineData("", "Url")]
    public async Task Create_InvalidUrl_ReturnsValidationError(string url, string field)
    {
        var result = await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(new CreateWebhook.Command("n8n", url, StageChanged));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
    }

    [Theory]
    [InlineData("http://n8n:5678/webhook/abc")]
    [InlineData("http://127.0.0.1:5678/webhook/abc")]
    public async Task Create_HttpForAllowedHost_Succeeds(string url)
    {
        var result = await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(new CreateWebhook.Command("n8n", url, StageChanged));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_NoOrUnknownEvents_ReturnsValidationError()
    {
        var none = await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(
            new CreateWebhook.Command("n8n", "https://n8n.example.test", []));
        var unknown = await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(
            new CreateWebhook.Command("n8n", "https://n8n.example.test", ["contact.merged"]));
        var noName = await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(
            new CreateWebhook.Command(" ", "https://n8n.example.test", StageChanged));

        none.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Events");
        unknown.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Events");
        noName.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public async Task Update_ExistingWebhook_ChangesFieldsAndKeepsSecret()
    {
        var created = await CreateAsync("https://n8n.example.test/a");
        string secretBefore;
        await using (var db = OpenDb())
        {
            secretBefore = (await db.WebhookSubscriptions.SingleAsync(Ct)).ProtectedSecret;
        }

        var result = await SendAsync<UpdateWebhook.Command, UpdateWebhook.Result>(
            new UpdateWebhook.Command(created.Id, "Zapier", "https://hooks.example.test/b", ["contact.created", "task.completed"], false));

        result.IsSuccess.Should().BeTrue();
        await using var verify = OpenDb();
        var stored = await verify.WebhookSubscriptions.SingleAsync(Ct);
        stored.Name.Should().Be("Zapier");
        stored.Url.Should().Be("https://hooks.example.test/b");
        stored.Events.Should().Equal("contact.created", "task.completed");
        stored.IsActive.Should().BeFalse();
        stored.ProtectedSecret.Should().Be(secretBefore);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var result = await SendAsync<UpdateWebhook.Command, UpdateWebhook.Result>(
            new UpdateWebhook.Command(Guid.NewGuid(), "n8n", "https://n8n.example.test", StageChanged, true));

        result.Error.Should().Be(WebhookErrors.NotFound);
    }

    [Fact]
    public async Task Update_InvalidUrl_ReturnsValidationError()
    {
        var created = await CreateAsync("https://n8n.example.test/a");

        var result = await SendAsync<UpdateWebhook.Command, UpdateWebhook.Result>(
            new UpdateWebhook.Command(created.Id, "n8n", "http://example.test", StageChanged, true));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Url");
    }

    [Fact]
    public async Task RegenerateSecret_ExistingWebhook_ReturnsNewSecret()
    {
        var created = await CreateAsync("https://n8n.example.test/a");

        var result = await SendAsync<RegenerateWebhookSecret.Command, RegenerateWebhookSecret.Result>(
            new RegenerateWebhookSecret.Command(created.Id));

        result.Value.Secret.Should().NotBe(created.Secret);
        await using var db = OpenDb();
        Get<ISecretProtector>().Unprotect((await db.WebhookSubscriptions.SingleAsync(Ct)).ProtectedSecret).Should().Be(result.Value.Secret);
    }

    [Fact]
    public async Task RegenerateSecret_UnknownOrEmptyId_ReturnsError()
    {
        var unknown = await SendAsync<RegenerateWebhookSecret.Command, RegenerateWebhookSecret.Result>(
            new RegenerateWebhookSecret.Command(Guid.NewGuid()));
        var empty = await SendAsync<RegenerateWebhookSecret.Command, RegenerateWebhookSecret.Result>(
            new RegenerateWebhookSecret.Command(Guid.Empty));

        unknown.Error.Should().Be(WebhookErrors.NotFound);
        empty.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Delete_WebhookWithDeliveries_RemovesWebhookAndLog()
    {
        _receiver = await WebhookReceiver.StartAsync();
        var created = await CreateAsync(_receiver.Url("hook"));
        await SendAsync<SendWebhookPing.Command, SendWebhookPing.Result>(new SendWebhookPing.Command(created.Id));

        var result = await SendAsync<DeleteWebhook.Command, DeleteWebhook.Result>(new DeleteWebhook.Command(created.Id));
        var again = await SendAsync<DeleteWebhook.Command, DeleteWebhook.Result>(new DeleteWebhook.Command(created.Id));

        result.IsSuccess.Should().BeTrue();
        again.Error.Should().Be(WebhookErrors.NotFound);
        await using var db = OpenDb();
        (await db.WebhookDeliveries.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Ping_ReachableReceiver_SendsSignedPingAndLogsDelivery()
    {
        _receiver = await WebhookReceiver.StartAsync();
        var created = await CreateAsync(_receiver.Url("hook"));

        var result = await SendAsync<SendWebhookPing.Command, SendWebhookPing.Result>(new SendWebhookPing.Command(created.Id));

        result.Value.Succeeded.Should().BeTrue();
        result.Value.StatusCode.Should().Be(204);
        var request = _receiver.Requests.Should().ContainSingle().Subject;
        request.EventType.Should().Be(WebhookEvents.Ping);
        WebhookSignature.Verify(created.Secret, request.Timestamp, request.Body, request.Signature, Start, TimeSpan.FromMinutes(5))
            .Should().BeTrue();
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("type").GetString().Should().Be(WebhookEvents.Ping);
        body.RootElement.GetProperty("data").GetProperty("subscriptionId").GetGuid().Should().Be(created.Id);

        var log = await QueryAsync<GetWebhookDeliveries.Query, GetWebhookDeliveries.Result>(new GetWebhookDeliveries.Query(created.Id));
        var delivery = log.Value.Items.Should().ContainSingle().Subject;
        delivery.EventType.Should().Be(WebhookEvents.Ping);
        delivery.Succeeded.Should().BeTrue();
        delivery.AttemptedAt.Should().Be(Start);
    }

    [Fact]
    public async Task Ping_FailingReceiver_ReportsAndLogsFailure()
    {
        _receiver = await WebhookReceiver.StartAsync();
        _receiver.StatusByPath["hook"] = 500;
        var created = await CreateAsync(_receiver.Url("hook"));

        var result = await SendAsync<SendWebhookPing.Command, SendWebhookPing.Result>(new SendWebhookPing.Command(created.Id));

        result.Value.Succeeded.Should().BeFalse();
        result.Value.StatusCode.Should().Be(500);
        result.Value.Error.Should().Be("HTTP 500");
        var webhooks = await QueryAsync<GetWebhooks.Query, GetWebhooks.Result>(new GetWebhooks.Query());
        webhooks.Value.Items.Single().LastSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Ping_UnreachableReceiver_ReportsConnectionError()
    {
        var created = await CreateAsync("http://127.0.0.1:9/hook");

        var result = await SendAsync<SendWebhookPing.Command, SendWebhookPing.Result>(new SendWebhookPing.Command(created.Id));

        result.Value.Succeeded.Should().BeFalse();
        result.Value.StatusCode.Should().BeNull();
        result.Value.Error.Should().StartWith("Verbindungsfehler");
    }

    [Fact]
    public async Task GetWebhooks_SeveralWebhooks_ReturnsThemByNameWithoutDeliveries()
    {
        await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(new CreateWebhook.Command("Zapier", "https://z.example.test", StageChanged));
        await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(new CreateWebhook.Command("n8n", "https://n.example.test", StageChanged));

        var result = await QueryAsync<GetWebhooks.Query, GetWebhooks.Result>(new GetWebhooks.Query());

        result.Value.Items.Select(i => i.Name).Should().Equal("n8n", "Zapier");
        result.Value.Items.Should().AllSatisfy(i => i.LastAttemptAt.Should().BeNull());
    }

    [Fact]
    public async Task GetDeliveries_UnknownWebhookOrInvalidLimit_ReturnsError()
    {
        var created = await CreateAsync("https://n8n.example.test/a");

        var unknown = await QueryAsync<GetWebhookDeliveries.Query, GetWebhookDeliveries.Result>(new GetWebhookDeliveries.Query(Guid.NewGuid()));
        var limit = await QueryAsync<GetWebhookDeliveries.Query, GetWebhookDeliveries.Result>(new GetWebhookDeliveries.Query(created.Id, 0));

        unknown.Error.Should().Be(WebhookErrors.NotFound);
        limit.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Limit");
    }

    public override async ValueTask DisposeAsync()
    {
        if (_receiver is not null)
        {
            await _receiver.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    private async Task<CreateWebhook.Result> CreateAsync(string url)
    {
        var result = await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(new CreateWebhook.Command("n8n", url, StageChanged));
        return result.Value;
    }
}
