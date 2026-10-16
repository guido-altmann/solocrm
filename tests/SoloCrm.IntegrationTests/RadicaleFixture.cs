using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace SoloCrm.IntegrationTests;

/// <summary>
/// A Radicale CardDAV/CalDAV server for the Nextcloud sync tests (ADR-014). Authentication is off: every user name
/// is accepted and gets its own collections, so tests isolate themselves by using a fresh user name.
/// </summary>
public sealed class RadicaleFixture : IAsyncLifetime
{
    private const int Port = 5232;

    private readonly IContainer _container = new ContainerBuilder("tomsquest/docker-radicale:3.5.4.0")
        .WithPortBinding(Port, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(Port).ForPath("/.web/")))
        .Build();

    /// <summary>Base address of the server, e.g. <c>http://localhost:32768</c>.</summary>
    public Uri BaseUrl => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}");

    public string Host => _container.Hostname;

    /// <summary>A handler for talking to Radicale that never follows redirects (like production) and rides out dropped connections.</summary>
    public static HttpMessageHandler CreateHandler() =>
        new DroppedConnectionRetryHandler { InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false } };

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Radicale answers with HTTP/1.0 and closes every connection, so each request opens a fresh one. Under the load of
    /// the full suite the Docker port proxy occasionally accepts such a connection and closes it without a response
    /// (<see cref="HttpRequestError.ResponseEnded"/>), locally and on the CI runner alike. The request never reached
    /// Radicale (nothing in its log), so sending it again is safe.
    /// </summary>
    private sealed class DroppedConnectionRetryHandler : DelegatingHandler
    {
        private const int MaxAttempts = 3;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await base.SendAsync(request, cancellationToken);
                }
                catch (HttpRequestException ex) when (attempt < MaxAttempts && ex.HttpRequestError == HttpRequestError.ResponseEnded)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                }
            }
        }
    }
}

/// <summary>
/// The tests against Radicale share one server and run after the parallel tests: under the load of the full suite,
/// connections to the container were occasionally dropped before reaching Radicale (nothing in its log).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RadicaleTestGroup : ICollectionFixture<RadicaleFixture>
{
    public const string Name = "Radicale";
}
