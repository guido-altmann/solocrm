using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application;

public static class DependencyInjection
{
    private static readonly Type[] HandlerInterfaces = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddValidatorsFromAssembly(assembly);
        services.AddHandlersFromAssembly(assembly);
        return services;
    }

    /// <summary>
    /// Registers every concrete class implementing <see cref="ICommandHandler{TCommand,TResult}"/>
    /// or <see cref="IQueryHandler{TQuery,TResult}"/> under its handler interface(s).
    /// </summary>
    public static IServiceCollection AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var registrations =
            from type in assembly.GetTypes()
            where type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
            from @interface in type.GetInterfaces()
            where @interface.IsGenericType && HandlerInterfaces.Contains(@interface.GetGenericTypeDefinition())
            select (@interface, type);

        foreach (var (@interface, type) in registrations)
        {
            services.AddTransient(@interface, type);
        }

        return services;
    }
}
