using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.CalendarFeeds;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Web.Endpoints;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>The feed URL needs no login, but only the current token works (SOL-6).</summary>
[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed class CalendarFeedEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private CrmWebApplicationFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await CreateDatabaseAsync(postgres, Ct);
        _factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Get_ValidTokenAnonymous_ReturnsIcsWithoutCaching()
    {
        var token = await GenerateAsync();
        await CreateTaskAsync("Angebot nachfassen");
        using var client = CreateClient();

        var response = await client.GetAsync(CalendarFeedEndpoints.FeedPath(token), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().Should().Be(CalendarFeedEndpoints.ContentType);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().StartWith("BEGIN:VCALENDAR").And.Contain("SUMMARY:Angebot nachfassen");
    }

    [Theory]
    [InlineData("0123456789012345678901234567890123456789012")]
    [InlineData("short")]
    public async Task Get_UnknownToken_Returns404(string token)
    {
        await GenerateAsync();
        using var client = CreateClient();

        var response = await client.GetAsync(CalendarFeedEndpoints.FeedPath(token), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_ReplacedToken_Returns404()
    {
        var old = await GenerateAsync();
        var current = await GenerateAsync();
        using var client = CreateClient();

        (await client.GetAsync(CalendarFeedEndpoints.FeedPath(old), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(CalendarFeedEndpoints.FeedPath(current), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string> GenerateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<GenerateCalendarFeed.Command, GenerateCalendarFeed.Result>>();
        return (await handler.Handle(new GenerateCalendarFeed.Command(), Ct)).Value.Token;
    }

    private async Task CreateTaskAsync(string title)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateTask.Command, CreateTask.Result>>();
        (await handler.Handle(new CreateTask.Command(title, new DateOnly(2026, 10, 20)), Ct)).IsSuccess.Should().BeTrue();
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
