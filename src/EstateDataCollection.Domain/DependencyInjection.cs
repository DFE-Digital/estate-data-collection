using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.DependencyInjection;

namespace EstateDataCollection.Domain;

public static class DependencyInjection
{
    public static IServiceCollection AddDomain(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblies(typeof(DependencyInjection).Assembly)
            .AddClasses(classes => classes.AssignableTo<IDomain>())
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo<IInstrumentation>())
            .AsSelf()
            .WithTransientLifetime());

        return services;
    }
}
