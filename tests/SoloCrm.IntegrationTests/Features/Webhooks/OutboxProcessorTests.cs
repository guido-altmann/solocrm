using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Webhooks;
using SoloCrm.Domain.Webhooks;
using SoloCrm.Infrastructure.Persistence.Outbox;

namespace SoloCrm.IntegrationTests.Features.Webhooks;

/// <summary>Outbox processing and webhook delivery (ADR-008, ADR-010, US-18 AK2/AK3).</summary>
public sealed class OutboxProcessorTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private static readonly string[] ContactCreated = ["contact.created"];

    private WebhookReceiver _receiver = null!;

    protected override IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?> { ["Webhooks:AllowedHttpHosts"] = "127.0.0.1", ["Outbox:BatchSize"] = "5" };

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _receiver = await WebhookReceiver.StartAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await _receiver.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ProcessDue_NoSubscription_MarksMessageProcessedWithoutDelivery()
    {
        await CreateContactAsync();

        await ProcessAsync();

        await using var db = OpenDb();
        var message = await db.OutboxMessages.SingleAsync(Ct);
        message.ProcessedAt.Should().Be(Start);
        message.Attempts.Should().Be(0);
        _receiver.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDue_MatchingSubscription_DeliversSignedPayloadWithIdsOnly()
    {
        var webhook = await CreateWebhookAsync("hook");
        var contactId = await CreateContactAsync();
        Time.Advance(TimeSpan.FromSeconds(3));

        await ProcessAsync();

        var request = _receiver.Requests.Should().ContainSingle().Subject;
        request.EventType.Should().Be("contact.created");
        WebhookSignature.Verify(webhook.Secret, request.Timestamp, request.Body, request.Signature, Time.GetUtcNow(), TimeSpan.FromMinutes(5))
            .Should().BeTrue();
        WebhookSignature.Verify("other-secret", request.Timestamp, request.Body, request.Signature, Time.GetUtcNow(), TimeSpan.FromMinutes(5))
            .Should().BeFalse();

        await using var db = OpenDb();
        var message = await db.OutboxMessages.SingleAsync(Ct);
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("id").GetGuid().Should().Be(message.Id);
        body.RootElement.GetProperty("type").GetString().Should().Be("contact.created");
        body.RootElement.GetProperty("occurredAt").GetString().Should().Be("2026-09-29T08:00:00Z");
        body.RootElement.GetProperty("data").EnumerateObject().Select(p => p.Name).Should().Equal("contactId");
        body.RootElement.GetProperty("data").GetProperty("contactId").GetGuid().Should().Be(contactId);
        request.Body.Should().NotContain("Lovelace");

        message.ProcessedAt.Should().Be(Time.GetUtcNow());
        message.Attempts.Should().Be(1);
        message.LastError.Should().BeNull();
        var delivery = await db.WebhookDeliveries.SingleAsync(Ct);
        delivery.SubscriptionId.Should().Be(webhook.Id);
        delivery.EventId.Should().Be(message.Id);
        delivery.Attempt.Should().Be(1);
        delivery.StatusCode.Should().Be(204);
        delivery.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessDue_SubscriptionCreatedAfterEventOrInactiveOrOtherEvent_SkipsMessage()
    {
        await CreateWebhookAsync("other-event", ["task.completed"]);
        var inactive = await CreateWebhookAsync("inactive");
        await SendAsync<UpdateWebhook.Command, UpdateWebhook.Result>(
            new UpdateWebhook.Command(inactive.Id, "inactive", _receiver.Url("inactive"), ContactCreated, false));
        Time.Advance(TimeSpan.FromMinutes(1));
        await CreateContactAsync();
        Time.Advance(TimeSpan.FromMinutes(1));
        await CreateWebhookAsync("later");

        await ProcessAsync();

        _receiver.Requests.Should().BeEmpty();
        await using var db = OpenDb();
        (await db.OutboxMessages.SingleAsync(Ct)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessDue_ReceiverKeepsFailing_RetriesWithBackoffAndGivesUpAfterSixAttempts()
    {
        _receiver.StatusByPath["hook"] = 503;
        await CreateWebhookAsync("hook");
        await CreateContactAsync();

        var schedule = new List<DateTimeOffset>();
        for (var attempt = 1; attempt <= WebhookRetryPolicy.MaxAttempts; attempt++)
        {
            await ProcessAsync();
            await using var db = OpenDb();
            var message = await db.OutboxMessages.SingleAsync(Ct);
            message.Attempts.Should().Be(attempt);
            message.LastError.Should().Be("hook: HTTP 503");
            if (attempt < WebhookRetryPolicy.MaxAttempts)
            {
                message.ProcessedAt.Should().BeNull();
                schedule.Add(message.NextAttemptAt);

                // Not due yet: nothing happens.
                Time.SetUtcNow(message.NextAttemptAt - TimeSpan.FromSeconds(1));
                await ProcessAsync();
                Time.SetUtcNow(message.NextAttemptAt);
            }
            else
            {
                message.ProcessedAt.Should().Be(Time.GetUtcNow());
            }
        }

        schedule.Zip(schedule.Prepend(Start), (next, previous) => next - previous)
            .Should().Equal(WebhookRetryPolicy.Delays);
        _receiver.Requests.Should().HaveCount(WebhookRetryPolicy.MaxAttempts);
        await using var verify = OpenDb();
        (await verify.WebhookDeliveries.OrderBy(d => d.Attempt).Select(d => d.Attempt).ToListAsync(Ct))
            .Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public async Task ProcessDue_OneOfTwoReceiversFails_RetriesOnlyTheFailedOne()
    {
        _receiver.StatusByPath["failing"] = 500;
        await CreateWebhookAsync("working");
        await CreateWebhookAsync("failing");
        await CreateContactAsync();

        await ProcessAsync();
        _receiver.StatusByPath["failing"] = 200;
        Time.Advance(WebhookRetryPolicy.Delays[0]);
        await ProcessAsync();

        _receiver.Requests.Select(r => r.Path).Should().BeEquivalentTo(["/working", "/failing", "/failing"]);
        await using var db = OpenDb();
        var message = await db.OutboxMessages.SingleAsync(Ct);
        message.ProcessedAt.Should().NotBeNull();
        message.LastError.Should().BeNull();
    }

    [Fact]
    public async Task ProcessDue_TwoProcessorsInParallel_DeliverEachMessageExactlyOnce()
    {
        await CreateWebhookAsync("hook");
        for (var i = 0; i < 40; i++)
        {
            await CreateContactAsync();
        }

        // Two instances with their own connections, like two containers during a rolling update (ADR-009).
        var first = ActivatorUtilities.CreateInstance<OutboxProcessor>(Services);
        var second = ActivatorUtilities.CreateInstance<OutboxProcessor>(Services);
        var claimed = new ConcurrentBag<int>();
        await Task.WhenAll(DrainAsync(first, claimed), DrainAsync(second, claimed));

        claimed.Sum().Should().Be(40);
        _receiver.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("id").GetGuid())
            .Should().HaveCount(40).And.OnlyHaveUniqueItems();
        await using var db = OpenDb();
        (await db.OutboxMessages.CountAsync(m => m.ProcessedAt == null, Ct)).Should().Be(0);
    }

    [Fact]
    public async Task ProcessDue_ClaimedByOtherProcessor_IsInvisibleUntilLeaseExpires()
    {
        await CreateWebhookAsync("hook");
        await CreateContactAsync();
        await using (var db = OpenDb())
        {
            // Simulates a processor that claimed the message and crashed before finishing it.
            await db.OutboxMessages.ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, Start + TimeSpan.FromMinutes(5)), Ct);
        }

        await ProcessAsync();
        _receiver.Requests.Should().BeEmpty();

        Time.Advance(TimeSpan.FromMinutes(5));
        await ProcessAsync();
        _receiver.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Cleanup_EntriesOlderThanRetention_AreDeletedAndNewerOnesKept()
    {
        await CreateWebhookAsync("hook");
        await CreateContactAsync();
        await ProcessAsync();
        Time.Advance(TimeSpan.FromDays(29));
        await CreateContactAsync();
        await ProcessAsync();
        await CreateContactAsync();

        Time.Advance(TimeSpan.FromDays(2));
        await Get<OutboxProcessor>().CleanupAsync(Ct);

        await using var db = OpenDb();
        (await db.OutboxMessages.CountAsync(Ct)).Should().Be(2, "the second message is younger and the third is unprocessed");
        (await db.WebhookDeliveries.CountAsync(Ct)).Should().Be(1);
    }

    private IServiceProvider Services => Get<IServiceProvider>();

    private static async Task DrainAsync(OutboxProcessor processor, ConcurrentBag<int> claimed)
    {
        int count;
        do
        {
            count = await processor.ProcessDueAsync(Ct);
            claimed.Add(count);
        }
        while (count > 0);
    }

    private async Task ProcessAsync()
    {
        while (await Get<OutboxProcessor>().ProcessDueAsync(Ct) > 0)
        {
        }
    }

    private async Task<CreateWebhook.Result> CreateWebhookAsync(string path, string[]? events = null)
    {
        var result = await SendAsync<CreateWebhook.Command, CreateWebhook.Result>(
            new CreateWebhook.Command(path, _receiver.Url(path), events ?? ContactCreated));
        return result.Value;
    }

    private async Task<Guid> CreateContactAsync()
    {
        var result = await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace"));
        return result.Value.Id;
    }
}
