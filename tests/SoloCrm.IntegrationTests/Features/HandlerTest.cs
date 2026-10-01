using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SoloCrm.Application.Abstractions;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.IntegrationTests.Web;

namespace SoloCrm.IntegrationTests.Features;

/// <summary>
/// Runs handlers through the production DI setup against a freshly migrated database per test.
/// </summary>
[Trait("Category", "Integration")]
public abstract class HandlerTest(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    protected static readonly DateTimeOffset Start = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    private ServiceProvider? _services;

    protected FakeTimeProvider Time { get; } = new(Start);

    protected string ConnectionString { get; private set; } = "";

    /// <summary>Additional configuration values for the service provider.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public virtual async ValueTask InitializeAsync()
    {
        ConnectionString = await CrmWebApplicationFactory.CreateDatabaseAsync(postgres, Ct);
        _services = CrmServices.Create(ConnectionString, Time, Settings);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    protected T Get<T>()
        where T : notnull => _services!.GetRequiredService<T>();

    protected Task<Result<TResult>> SendAsync<TCommand, TResult>(TCommand command) =>
        Get<ICommandHandler<TCommand, TResult>>().Handle(command, Ct);

    protected Task<Result<TResult>> QueryAsync<TQuery, TResult>(TQuery query) =>
        Get<IQueryHandler<TQuery, TResult>>().Handle(query, Ct);

    /// <summary>A context without interceptors for arranging and verifying data.</summary>
    protected CrmDbContext OpenDb() => CrmWebApplicationFactory.CreateDbContext(ConnectionString);
}
