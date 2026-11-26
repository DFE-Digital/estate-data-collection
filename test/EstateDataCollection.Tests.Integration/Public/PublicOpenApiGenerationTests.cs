using System.Text.Json;
using AwesomeAssertions;
using AzureFunctions.Extensions.Swashbuckle;
using EstateDataCollection.Application;
using EstateDataCollection.AzureFunctions.Public.OpenApi;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Web;
using EstateDataCollection.Domain;
using EstateDataCollection.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Scalar.Azure.Functions;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Public;

public sealed class PublicOpenApiGenerationTests
{
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "EstateDataCollection.AzureFunctions.Public";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider BuildPublicServiceProvider(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["AzureWebJobsStorage"] = "UseDevelopmentStorage=true",
            ["Blob:Schema:ContainerName"] = "schemas",
            ["Blob:Schema:DraftContainerName"] = "draft-schemas",
            ["Blob:Submission:PayloadContainerName"] = "payloads",
            ["Blob:Submission:ProcessedContainerName"] = "processed",
            ["OpenApi:Docs:FaUApiGatewayUrl"] = "https://api.education.gov.uk/estate-data-collection/v1"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddFunctions(typeof(GetSchemasEndpoint).Assembly);
        services.AddPublicOpenApiDocs(config);
        services.AddApplication();
        services.AddDomain();
        services.AddInfrastructure(config);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public async Task SwashBuckleClient_Should_Generate_SubscriptionKey_First_OpenApi_3_0_By_Default()
    {
        using var provider = BuildPublicServiceProvider();
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ISwashBuckleClient>();
        var scalar = scope.ServiceProvider.GetRequiredService<IScalarApiReference>();
        scalar.Should().NotBeNull();

        await using var jsonStream = await client.GetSwaggerJsonDocumentAsync("http://localhost:7071", "v1");
        using var jsonReader = new StreamReader(jsonStream);
        var json = await jsonReader.ReadToEndAsync();

        json.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("openapi").GetString().Should().StartWith("3.0.");

        var infoDescription = root.GetProperty("info").GetProperty("description").GetString();
        infoDescription.Should().Contain("## Authentication");
        infoDescription.Should().Contain("## Submission Lifecycle");
        infoDescription.Should().Contain("Ocp-Apim-Subscription-Key");

        // When WikiBaseUrl is not configured, externalDocs should not be emitted
        root.TryGetProperty("externalDocs", out _).Should().BeFalse();

        // Tags should flow in end-to-end consumer order: Schema Discovery -> Submissions -> Diagnostics
        var tagNames = root.GetProperty("tags")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();
        tagNames.Should().Equal("Schema Discovery", "Submissions", "Diagnostics");

        var servers = root.GetProperty("servers").EnumerateArray().Select(s => s.GetProperty("url").GetString()).ToList();
        servers.Should().Contain("https://api.education.gov.uk/estate-data-collection/v1");
        servers.Should().Contain("/api");

        var securitySchemes = root.GetProperty("components").GetProperty("securitySchemes");
        securitySchemes.TryGetProperty(FaUApiOpenApiDocumentFilter.SubscriptionKeySchemeId, out _).Should().BeTrue();
        securitySchemes.TryGetProperty(FaUApiOpenApiDocumentFilter.OAuth2SchemeId, out _).Should().BeFalse();

        // /about is anonymous; protected endpoints require SubscriptionKey standalone
        var schemasSecurity = root.GetProperty("paths")
            .GetProperty("/schemas")
            .GetProperty("get")
            .GetProperty("security")
            .EnumerateArray()
            .ToList();
        schemasSecurity.Should().ContainSingle();
        schemasSecurity[0].TryGetProperty(FaUApiOpenApiDocumentFilter.SubscriptionKeySchemeId, out _).Should().BeTrue();

        var aboutSecurity = root.GetProperty("paths")
            .GetProperty("/about")
            .GetProperty("get")
            .GetProperty("security")
            .EnumerateArray()
            .ToList();
        aboutSecurity.Should().BeEmpty();

        // Paths and per-path operations should follow integration workflow order
        var pathKeys = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        pathKeys.Should().Equal(
            "/schemas",
            "/schemas/{schemaName}",
            "/schemas/{schemaName}/versions/{version}",
            "/submissions/{schemeName}",
            "/submissions/{submissionId}",
            "/submissions/{submissionId}/check",
            "/submissions/{submissionId}/finalise",
            "/about");

        var submissionItemMethods = root.GetProperty("paths")
            .GetProperty("/submissions/{submissionId}")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToList();
        submissionItemMethods.Should().Equal("put", "get");

        // Documentation endpoints themselves must not appear in the generated API spec
        pathKeys.Should().NotContain(p => p.Contains("openapi", StringComparison.OrdinalIgnoreCase));
        pathKeys.Should().NotContain(p => p.Contains("scalar", StringComparison.OrdinalIgnoreCase));

        // Endpoint XML summaries, remarks, and responses should populate operation metadata
        var createSubmissionOp = root.GetProperty("paths")
            .GetProperty("/submissions/{schemeName}")
            .GetProperty("post");
        createSubmissionOp.GetProperty("summary").GetString().Should().Be("Create a new draft submission session.");
        createSubmissionOp.GetProperty("description").GetString().Should().Contain("pinned schema version");
        createSubmissionOp.GetProperty("responses").GetProperty("201").GetProperty("description").GetString()
            .Should().Contain("submissionId");

        // Multi-assembly XML docs should enrich Common.Web, Application, and Common schemas
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var apiResponseSchema = schemas.GetProperty("ApiResponseOfCreateSubmissionResult");
        apiResponseSchema.GetProperty("description").GetString().Should().Contain("Standard response envelope");
        apiResponseSchema.GetProperty("properties").GetProperty("successful").GetProperty("description").GetString()
            .Should().Contain("completed successfully");

        var createResultSchema = schemas.GetProperty("CreateSubmissionResult");
        createResultSchema.GetProperty("description").GetString().Should().Contain("draft submission session");
        createResultSchema.GetProperty("properties").GetProperty("submissionId").GetProperty("description").GetString()
            .Should().Contain("Unique identifier");

        var validationErrorSchema = schemas.GetProperty("ValidationError");
        validationErrorSchema.GetProperty("description").GetString().Should().Contain("validation error");
        validationErrorSchema.GetProperty("properties").GetProperty("propertyPath").GetProperty("description").GetString()
            .Should().Contain("JSON Pointer");

        // Internal Azure Function key query param and CancellationToken artifacts must not leak
        json.Should().NotContain("\"name\": \"code\"");
        json.Should().NotContain("CancellationToken");

        await using var yamlStream = await client.GetSwaggerYamlDocumentAsync("http://localhost:7071", "v1");
        using var yamlReader = new StreamReader(yamlStream);
        var yaml = await yamlReader.ReadToEndAsync();
        yaml.Should().Contain("openapi: 3.0.");
        yaml.Should().Contain("FaUApiSubscriptionKey:");
    }

    [Fact]
    public async Task SwashBuckleClient_When_OAuth2_Configured_Should_Include_OAuth2_As_Alternative_Requirement()
    {
        using var provider = BuildPublicServiceProvider(new Dictionary<string, string?>
        {
            ["OpenApi:Docs:FaUApiOAuthTokenUrl"] = "https://login.education.gov.uk/oauth2/v2.0/token",
            ["OpenApi:Docs:FaUApiOAuthScope"] = "https://api.education.gov.uk/edc/.default"
        });
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ISwashBuckleClient>();
        await using var jsonStream = await client.GetSwaggerJsonDocumentAsync("http://localhost:7071", "v1");
        using var jsonReader = new StreamReader(jsonStream);
        using var doc = JsonDocument.Parse(await jsonReader.ReadToEndAsync());

        var root = doc.RootElement;
        var securitySchemes = root.GetProperty("components").GetProperty("securitySchemes");
        securitySchemes.TryGetProperty(FaUApiOpenApiDocumentFilter.SubscriptionKeySchemeId, out _).Should().BeTrue();
        securitySchemes.TryGetProperty(FaUApiOpenApiDocumentFilter.OAuth2SchemeId, out _).Should().BeTrue();

        // Two separate requirements in array => SubscriptionKey standalone OR (SubscriptionKey + OAuth2)
        var schemasSecurity = root.GetProperty("paths")
            .GetProperty("/schemas")
            .GetProperty("get")
            .GetProperty("security")
            .EnumerateArray()
            .ToList();
        schemasSecurity.Should().HaveCount(2);
        schemasSecurity[0].EnumerateObject().Select(p => p.Name).Should().Equal(FaUApiOpenApiDocumentFilter.SubscriptionKeySchemeId);
        schemasSecurity[1].EnumerateObject().Select(p => p.Name).Should().Contain(
            [FaUApiOpenApiDocumentFilter.SubscriptionKeySchemeId, FaUApiOpenApiDocumentFilter.OAuth2SchemeId]);
    }

    [Fact]
    public async Task SwashBuckleClient_When_WikiBaseUrl_Configured_Should_Emit_ExternalDocs_On_Document_Tags_And_Operations()
    {
        using var provider = BuildPublicServiceProvider(new Dictionary<string, string?>
        {
            ["OpenApi:Docs:WikiBaseUrl"] = "https://github.com/DFE-Digital/estate-data-collection/wiki/"
        });
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ISwashBuckleClient>();
        await using var jsonStream = await client.GetSwaggerJsonDocumentAsync("http://localhost:7071", "v1");
        using var jsonReader = new StreamReader(jsonStream);
        using var doc = JsonDocument.Parse(await jsonReader.ReadToEndAsync());

        var root = doc.RootElement;
        var rootExternalDocs = root.GetProperty("externalDocs");
        rootExternalDocs.GetProperty("url").GetString()
            .Should().Be("https://github.com/DFE-Digital/estate-data-collection/wiki");
        rootExternalDocs.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();

        var tags = root.GetProperty("tags").EnumerateArray().ToList();
        var submissionsTag = tags.Single(t => t.GetProperty("name").GetString() == "Submissions");
        submissionsTag.GetProperty("externalDocs").GetProperty("url").GetString()
            .Should().Be("https://github.com/DFE-Digital/estate-data-collection/wiki/Submissions-Lifecycle");

        var createSubmissionOp = root.GetProperty("paths")
            .GetProperty("/submissions/{schemeName}")
            .GetProperty("post");
        createSubmissionOp.GetProperty("externalDocs").GetProperty("url").GetString()
            .Should().Be("https://github.com/DFE-Digital/estate-data-collection/wiki/Submissions-Lifecycle#create-submission");
    }
}
