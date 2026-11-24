using System.Reflection;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Events;
using EstateDataCollection.Common.Web.Middleware;
using EstateDataCollection.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.Common.Web;

public static class DependencyInjection
{
    public static FunctionsApplicationBuilder UseFunctionsMiddleware(this FunctionsApplicationBuilder builder)
    {
        builder.UseMiddleware<RequestSizeLimitMiddleware>();
        builder.UseMiddleware<GlobalExceptionMiddleware>();
        builder.UseMiddleware<VersionLoggingMiddleware>();
        return builder;
    }

    public static IServiceCollection AddFunctions(this IServiceCollection services, Assembly hostAssembly)
    {
        var commonAssembly = typeof(DependencyInjection).Assembly;

        services
            .AddConfigOptions(commonAssembly)
            .AddConfigOptions(hostAssembly)
            .Scan(scan => scan
                .FromAssemblies(commonAssembly, hostAssembly)
                .AddClasses(classes => classes.AssignableTo<IInstrumentation>())
                .AsSelf()
                .WithTransientLifetime()
                .AddClasses(classes => classes.AssignableTo<IFunctionEndpoint>())
                .AsSelf()
                .WithTransientLifetime()
                .AddClasses(classes => classes.AssignableTo<IFunctionEvent>())
                .AsSelf()
                .WithTransientLifetime());

        return services;
    }

    public static IServiceCollection AddLogging(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggingBuilder logging)
    {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);

        if (configuration is IConfigurationBuilder configBuilder)
        {
            configBuilder.AddAppConfiguration(services);
        }

        return services;
    }
}
