using System.Reflection;
using AwesomeAssertions;
using EstateDataCollection.Application;
using EstateDataCollection.AzureFunctions.Admin.Routes;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Web;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Events;
using EstateDataCollection.Domain;
using EstateDataCollection.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EstateDataCollection.Tests.Integration;

public class FunctionEndpointAndEventArchitectureTests
{
    public static IEnumerable<object[]> FunctionHostAssemblies()
    {
        yield return [typeof(VerifySchemaEndpoint).Assembly];
        yield return [typeof(GetSchemasEndpoint).Assembly];
    }

    [Theory]
    [MemberData(nameof(FunctionHostAssemblies))]
    public void Every_Azure_Function_Class_Should_Be_Sealed_Single_Operation_And_Implement_Endpoint_Or_Event_Contract(
        Assembly hostAssembly)
    {
        var functionTypes = hostAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Any(m => m.GetCustomAttribute<FunctionAttribute>() is not null))
            .ToList();

        functionTypes.Should().NotBeEmpty();

        foreach (var type in functionTypes)
        {
            type.IsSealed.Should().BeTrue($"{type.Name} should be sealed.");

            var implementsContract = typeof(IFunctionEndpoint).IsAssignableFrom(type) ||
                                     typeof(IFunctionEvent).IsAssignableFrom(type);
            implementsContract.Should().BeTrue(
                $"{type.Name} must implement IFunctionEndpoint or IFunctionEvent.");

            var functionMethods = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<FunctionAttribute>() is not null)
                .ToList();

            functionMethods.Should().ContainSingle(
                $"{type.Name} must contain exactly one [Function] entry point (single-operation pattern).");
        }
    }

    [Theory]
    [MemberData(nameof(FunctionHostAssemblies))]
    public void AddFunctions_Should_Resolve_All_Registered_Endpoints_And_Events_From_DI(Assembly hostAssembly)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
                ["BlobSchemaRegistry:ContainerName"] = "schemas",
                ["BlobSchemaRegistry:DraftContainerName"] = "draft-schemas",
                ["BlobSubmissionRepository:PayloadContainerName"] = "payloads",
                ["BlobSubmissionRepository:ProcessedContainerName"] = "processed"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddFunctions(hostAssembly);
        services.AddApplication();
        services.AddDomain();
        services.AddInfrastructure(config);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var endpointAndEventTypes = hostAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        (typeof(IFunctionEndpoint).IsAssignableFrom(t) || typeof(IFunctionEvent).IsAssignableFrom(t)))
            .ToList();

        endpointAndEventTypes.Should().NotBeEmpty();

        using var scope = provider.CreateScope();
        foreach (var type in endpointAndEventTypes)
        {
            var resolved = scope.ServiceProvider.GetRequiredService(type);
            resolved.Should().NotBeNull();
        }
    }
}
