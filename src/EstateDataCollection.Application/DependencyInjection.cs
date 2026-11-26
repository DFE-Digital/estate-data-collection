using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EstateDataCollection.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddScoped<ActorContext>();
        services.TryAddScoped<IActorContext>(sp => sp.GetRequiredService<ActorContext>());
        services.TryAddScoped<UserContext>();
        services.TryAddScoped<IUserContext>(sp => sp.GetRequiredService<UserContext>());

        services.Scan(scan => scan
            .FromAssemblies(typeof(DependencyInjection).Assembly)
            .AddClasses(classes => classes.AssignableTo<IApplication>())
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo<IUseCase>().Where(t => !t.IsGenericTypeDefinition))
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo<IInstrumentation>())
            .AsSelf()
            .WithTransientLifetime());

        services.Decorate(typeof(IUseCase<,>), typeof(SubmissionUseCaseAuditDecorator<,>));

        return services;
    }
}
