using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoloCrm.Application.Features.Webhooks;

namespace SoloCrm.IntegrationTests.Features.Webhooks;

/// <summary>
/// A local HTTP endpoint on a random port that records webhook requests and answers with a configurable status.
/// </summary>
public sealed class WebhookReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<ReceivedWebhook> _requests = new();

    private WebhookReceiver(WebApplication app)
    {
        _app = app;
        _app.MapPost("/{**path}", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();
            _requests.Enqueue(new ReceivedWebhook(
                request.Path,
                body,
                request.Headers[WebhookSignature.TimestampHeader].ToString(),
                request.Headers[WebhookSignature.SignatureHeader].ToString(),
                request.Headers[WebhookSignature.EventHeader].ToString()));
            return Results.StatusCode(StatusFor(request.Path));
        });
    }

    /// <summary>Status code per path; everything else answers 204.</summary>
    public ConcurrentDictionary<string, int> StatusByPath { get; } = new();

    public IReadOnlyList<ReceivedWebhook> Requests => [.. _requests];

    public string BaseUrl { get; private set; } = "";

    public static async Task<WebhookReceiver> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var receiver = new WebhookReceiver(builder.Build());
        await receiver._app.StartAsync();
        receiver.BaseUrl = receiver._app.Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        return receiver;
    }

    public string Url(string path) => $"{BaseUrl}/{path}";

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private int StatusFor(PathString path) =>
        StatusByPath.TryGetValue(path.Value!.TrimStart('/'), out var status) ? status : StatusCodes.Status204NoContent;
}

public sealed record ReceivedWebhook(string Path, string Body, string Timestamp, string Signature, string EventType);
