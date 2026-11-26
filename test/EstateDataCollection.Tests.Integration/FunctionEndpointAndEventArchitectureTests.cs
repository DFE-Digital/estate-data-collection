using System.Globalization;
using System.Reflection;
using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Events;
using EstateDataCollection.Common.Web.Security;
using EstateDataCollection.Domain;
using EstateDataCollection.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EstateDataCollection.Tests.Integration;

public class FunctionEndpointAndEventArchitectureTests
{
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "EstateDataCollection.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public static IEnumerable<object[]> FunctionHostAssemblies()
    {
        yield return [typeof(PostVerifySchemaEndpoint).Assembly];
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
    public void Every_Function_Class_Should_Follow_HttpMethod_And_Role_Naming_Conventions(Assembly hostAssembly)
    {
        var functionTypes = hostAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Any(m => m.GetCustomAttribute<FunctionAttribute>() is not null))
            .ToList();

        functionTypes.Should().NotBeEmpty();

        foreach (var type in functionTypes)
        {
            if (typeof(IFunctionEvent).IsAssignableFrom(type))
            {
                type.Name.Should().EndWith("Event",
                    $"{type.Name} implements IFunctionEvent and should be named *Event.");
                continue;
            }

            var trigger = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters())
                .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
                .FirstOrDefault(a => a is not null);

            trigger.Should().NotBeNull($"{type.Name} implements IFunctionEndpoint and must declare an [HttpTrigger].");
            trigger!.Methods.Should().ContainSingle(
                $"{type.Name} should declare a single HTTP method for unambiguous naming.");

            var expectedPrefix = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                trigger.Methods!.Single().ToLowerInvariant());

            type.Name.Should().StartWith(expectedPrefix,
                $"{type.Name} handles HTTP {expectedPrefix} and should start with '{expectedPrefix}'.");

            var isGetPage = typeof(FunctionPageBase).IsAssignableFrom(type) &&
                            string.Equals(expectedPrefix, "Get", StringComparison.Ordinal);
            var expectedSuffix = isGetPage ? "Page" : "Endpoint";

            type.Name.Should().EndWith(expectedSuffix,
                $"{type.Name} should end with '{expectedSuffix}'.");
        }
    }

    [Theory]
    [MemberData(nameof(FunctionHostAssemblies))]
    public void Every_BlobTrigger_Should_Use_EventGrid_Source_For_Flex_Consumption(Assembly hostAssembly)
    {
        var blobTriggers = hostAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters())
                .Select(p => (Type: t, Trigger: p.GetCustomAttribute<BlobTriggerAttribute>()))
                .Where(x => x.Trigger is not null))
            .ToList();

        foreach (var (type, trigger) in blobTriggers)
        {
            trigger!.Source.Should().Be(
                BlobTriggerSource.EventGrid,
                $"{type.Name} runs on Azure Functions Flex Consumption, which only supports EventGrid as the BlobTrigger source.");
        }
    }

    [Theory]
    [MemberData(nameof(FunctionHostAssemblies))]
    public void AddFunctions_Should_Resolve_All_Registered_Endpoints_And_Events_From_DI(Assembly hostAssembly)
    {
        var config = BuildBaseConfiguration();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddFunctions(hostAssembly);
        services.AddApplication();
        services.AddDomain();
        services.AddInfrastructure(config);

        if (hostAssembly == typeof(GetAdminSchemasEndpoint).Assembly)
        {
            services.AddScoped<RazorComponentRenderer>();
        }

        AssertAllEndpointsResolve(services, hostAssembly);
    }

    [Fact]
    public void AdminHost_Routes_Should_Partition_Api_Manage_And_Assets_With_Expected_Auth_Levels()
    {
        var adminAssembly = typeof(GetAdminSchemasEndpoint).Assembly;
        var triggers = adminAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => m.GetParameters()
                .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
                .FirstOrDefault(a => a is not null))
            .Where(a => a is not null)
            .Cast<HttpTriggerAttribute>()
            .ToList();

        triggers.Should().NotBeEmpty();

        foreach (var trigger in triggers)
        {
            var route = trigger.Route ?? string.Empty;
            if (route.StartsWith($"{AppRoutes.AdminApi.Prefix}/", StringComparison.Ordinal))
            {
                trigger.AuthLevel.Should().Be(AuthorizationLevel.Function, $"API route '{route}' should require Function auth level.");
            }
            else if (route.StartsWith(AppRoutes.AdminUi.Prefix, StringComparison.Ordinal) ||
                     route.StartsWith($"{AppRoutes.Assets.Prefix}/", StringComparison.Ordinal))
            {
                trigger.AuthLevel.Should().Be(AuthorizationLevel.Anonymous, $"UI route '{route}' should allow browser navigation.");
            }
            else
            {
                Assert.Fail($"Unexpected Admin host route '{route}'. Expected '{AppRoutes.AdminApi.Prefix}/...', '{AppRoutes.AdminUi.Prefix}...', or '{AppRoutes.Assets.Prefix}/...'.");
            }
        }
    }

    [Fact]
    public void Every_Admin_Endpoint_Should_Declare_AllowAnonymous_Or_RequireRoles()
    {
        var adminAssembly = typeof(GetAdminSchemasEndpoint).Assembly;
        var resolver = new FunctionAuthMetadataResolver(adminAssembly);

        var functionMethods = adminAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFunctionEndpoint).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<FunctionAttribute>() is not null)
            .ToList();

        functionMethods.Should().NotBeEmpty();

        foreach (var method in functionMethods)
        {
            var functionName = method.GetCustomAttribute<FunctionAttribute>()!.Name;
            var metadata = resolver.ResolveByFunctionName(functionName);

            var hasExplicitPolicy = metadata.AllowAnonymous || metadata.RoleRequirements.Count > 0;
            hasExplicitPolicy.Should().BeTrue(
                $"{method.DeclaringType?.Name} ({functionName}) must declare [AllowAnonymous] or at least one [RequireRoles(...)].");
        }
    }

    [Fact]
    public void Every_HttpTrigger_Route_Should_Be_Declared_In_AppRoutes_Templates()
    {
        var adminTemplates = GetTemplateConstants(
            typeof(AppRoutes.AdminUi.Templates),
            typeof(AppRoutes.AdminApi.Templates),
            typeof(AppRoutes.Assets.Templates));
        var publicTemplates = GetTemplateConstants(typeof(AppRoutes.PublicApi.Templates));

        foreach (var (endpointType, trigger) in GetTriggers(typeof(GetAdminSchemasEndpoint).Assembly))
        {
            trigger.Route.Should().NotBeNullOrWhiteSpace($"{endpointType.Name} must declare a non-empty route.");
            adminTemplates.Should().Contain(trigger.Route!, $"{endpointType.Name} route must come from AppRoutes.");
        }

        foreach (var (endpointType, trigger) in GetTriggers(typeof(GetSchemasEndpoint).Assembly))
        {
            trigger.Route.Should().NotBeNullOrWhiteSpace($"{endpointType.Name} must declare a non-empty route.");
            publicTemplates.Should().Contain(trigger.Route!, $"{endpointType.Name} route must come from AppRoutes.PublicApi.Templates.");
        }
    }

    private static HashSet<string> GetTemplateConstants(params Type[] templateTypes)
    {
        return templateTypes
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static List<(Type EndpointType, HttpTriggerAttribute Trigger)> GetTriggers(Assembly hostAssembly)
    {
        return hostAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFunctionEndpoint).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters())
                .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
                .Where(a => a is not null)
                .Select(a => (t, a!)))
            .ToList();
    }

    private static IConfiguration BuildBaseConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
                ["BlobSchemaRegistry:ContainerName"] = "schemas",
                ["BlobSchemaRegistry:DraftContainerName"] = "draft-schemas",
                ["BlobSubmissionRepository:PayloadContainerName"] = "payloads",
                ["BlobSubmissionRepository:ProcessedContainerName"] = "processed"
            })
            .Build();
    }

    private static void AssertAllEndpointsResolve(IServiceCollection services, Assembly hostAssembly)
    {
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
