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

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
