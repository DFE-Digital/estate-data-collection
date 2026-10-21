using System.Reflection;
using EstateDataCollection.Common.Markers;
using Microsoft.Extensions.DependencyInjection;

namespace EstateDataCollection.Infrastructure.Configuration;

public static class ConfigOptionsExtensions
{
    public static IServiceCollection AddConfigOptions(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        // Use Scrutor purely as a type finder
        var found = new ServiceCollection();
        found.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(c => c.AssignableTo(typeof(IOption<>)), publicOnly: false)
            .AsSelf());

        var register = typeof(ConfigOptionsExtensions)
            .GetMethod(nameof(Register), BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var descriptor in found)
        {
            register.MakeGenericMethod(descriptor.ServiceType).Invoke(null, [services]);
        }

        return services;
    }

    private static void Register<T>(IServiceCollection services)
        where T : class, IOption<T>
    {
        services.AddOptions<T>()
            .BindConfiguration(T.SectionName) 
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}