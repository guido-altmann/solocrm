using Microsoft.Extensions.DependencyInjection;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddHandlersFromAssembly_CommandAndQueryHandlers_AreResolvable()
    {
        var services = new ServiceCollection();

        services.AddHandlersFromAssembly(typeof(DependencyInjectionTests).Assembly);
        using var provider = services.BuildServiceProvider();

        provider.GetService<ICommandHandler<Ping.Command, Ping.Result>>().Should().BeOfType<Ping.Handler>();
        provider.GetService<IQueryHandler<Ping.Query, Ping.Result>>().Should().BeOfType<Ping.QueryHandler>();
    }

    [Fact]
    public void AddHandlersFromAssembly_AbstractHandler_IsNotRegistered()
    {
        var services = new ServiceCollection();

        services.AddHandlersFromAssembly(typeof(DependencyInjectionTests).Assembly);

        services.Should().NotContain(d => d.ImplementationType == typeof(Ping.AbstractHandler));
    }

    [Fact]
    public void AddApplication_Always_Succeeds()
    {
        var services = new ServiceCollection();

        var act = () => services.AddApplication();

        act.Should().NotThrow();
    }

    /// <summary>Slice-shaped sample to verify the scan and nested Result naming.</summary>
    public static class Ping
    {
        public sealed record Command;
        public sealed record Query;
        public sealed record Result(string Message);

        public sealed class Handler : ICommandHandler<Command, Result>
        {
            public Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Result>>(new Result("pong"));
        }

        public sealed class QueryHandler : IQueryHandler<Query, Result>
        {
            public Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken) =>
                Task.FromResult<Result<Result>>(new Result("pong"));
        }

        public abstract class AbstractHandler : ICommandHandler<Command, Result>
        {
            public abstract Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken);
        }
    }
}
