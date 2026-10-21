using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.DependencyInjection;

namespace EstateDataCollection.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblies(typeof(DependencyInjection).Assembly)
            .AddClasses(classes => classes.AssignableTo<IApplication>())
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo<IUseCase>())
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo<IInstrumentation>())
            .AsSelf()
            .WithTransientLifetime());

        return services;
    }
}
