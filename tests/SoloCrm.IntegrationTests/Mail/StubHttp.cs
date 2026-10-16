using System.Net;
using System.Text;

namespace SoloCrm.IntegrationTests.Mail;

/// <summary>A scripted HTTP server for the Graph and DAV tests: answers requests in order and records them.</summary>
internal sealed class StubHttp : HttpMessageHandler, IHttpClientFactory
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public void Respond(HttpStatusCode status, string json) =>
        _responses.Enqueue(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public void Respond(Func<HttpRequestMessage, HttpResponseMessage> respond) => _responses.Enqueue(respond);

    public void Throw(Exception exception) => _responses.Enqueue(_ => throw exception);

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("Prefer", out var prefer) ? string.Join(',', prefer) : null,
            body));
        return _responses.Dequeue()(request);
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Prefer, string? Body);
}
