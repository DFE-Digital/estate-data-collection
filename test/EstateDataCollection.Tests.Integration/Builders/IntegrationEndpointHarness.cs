using Azure.Storage.Blobs;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Web;
using EstateDataCollection.Domain;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EstateDataCollection.Tests.Integration.Builders;

/// <summary>
/// DI-backed test harness that mirrors host composition against Azurite-backed containers
/// and lazily resolves any Admin/Public endpoint, event, use case, or infrastructure service via <see cref="Get{T}"/>.
/// </summary>
public class IntegrationEndpointHarness : IDisposable, IAsyncDisposable
{
    private readonly ServiceProvider _rootProvider;
    private readonly IServiceScope _scope;

    public AzuriteFixture Fixture { get; }
    public BlobServiceClient BlobClient
    {
        get { return Fixture.Client; }
    }

    public string SchemasContainerName
    {
        get { return Fixture.SchemasContainerName; }
    }

    public string DraftSchemasContainerName
    {
        get { return Fixture.DraftSchemasContainerName; }
    }

    public string PayloadsContainerName
    {
        get { return Fixture.PayloadsContainerName; }
    }

    public string ProcessedContainerName
    {
        get { return Fixture.ProcessedContainerName; }
    }

    public IntegrationEndpointHarness(
        AzuriteFixture fixture,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        Fixture = fixture;

        var configValues = new Dictionary<string, string?>
        {
            [$"{BlobSchemaRegistryOptions.SectionName}:ContainerName"] = fixture.SchemasContainerName,
            [$"{BlobSchemaRegistryOptions.SectionName}:DraftContainerName"] = fixture.DraftSchemasContainerName,
            [$"{BlobSubmissionRepositoryOptions.SectionName}:PayloadContainerName"] = fixture.PayloadsContainerName,
            [$"{BlobSubmissionRepositoryOptions.SectionName}:ProcessedContainerName"] = fixture.ProcessedContainerName,
            [$"{ValidationOptions.SectionName}:UnknownEngineBehavior"] = nameof(UnknownEngineBehavior.Fail),
            [$"{ValidationOptions.SectionName}:RequireUrnOnFinalise"] = bool.TrueString
        };

        if (configurationOverrides is not null)
        {
            foreach (var (key, value) in configurationOverrides)
            {
                configValues[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDomain();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.RemoveAll<BlobServiceClient>();
        services.AddSingleton(fixture.Client);
        services.AddScoped<RazorComponentRenderer>();
        services.AddFunctions(typeof(PostVerifySchemaEndpoint).Assembly);
        services.AddFunctions(typeof(PostCreateSubmissionEndpoint).Assembly);

        configureServices?.Invoke(services);

        _rootProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        _scope = _rootProvider.CreateScope();
    }

    public T Get<T>() where T : notnull
    {
        return _scope.ServiceProvider.GetRequiredService<T>();
    }

    public static string UniqueName(string prefix)
    {
        return $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, 48)];
    }

    public async Task<SubmitSchemaResult> SubmitAndActivateSchemaAsync(
        string schemaName,
        int version,
        string schemaJson,
        SchemaIdentifierConfig? identifierConfig = null,
        string? rulesJson = null)
    {
        var submitRes = await Get<PostSubmitSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                SchemaJson: schemaJson,
                RulesJson: rulesJson,
                IdentifierConfig: identifierConfig)),
            schemaName,
            version,
            CancellationToken.None);
        var submitted = submitRes.ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);

        var schedRes = await Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30),
                LifecycleAction: "Activate",
                IdentifierConfig: identifierConfig)),
            schemaName,
            version,
            CancellationToken.None);
        schedRes.ShouldBeApiSuccess<ScheduleSchemaResult>(StatusCodes.Status200OK);

        return submitted;
    }

    public async Task<Guid> CreateDraftSubmissionAsync(string schemaName, int? version = null)
    {
        var builder = new HttpRequestBuilder();
        if (version.HasValue)
        {
            builder.WithQuery("version", version.Value.ToString());
        }

        var res = await Get<PostCreateSubmissionEndpoint>().HandleAsync(builder.Build(), schemaName, CancellationToken.None);
        return res.ShouldBeApiSuccess<CreateSubmissionResult>(StatusCodes.Status201Created).SubmissionId;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _rootProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_scope is IAsyncDisposable asyncScope)
        {
            await asyncScope.DisposeAsync();
        }
        else
        {
            _scope.Dispose();
        }

        await _rootProvider.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Typed convenience wrapper for integration tests focused on a single endpoint, page, or event.
/// </summary>
public sealed class IntegrationEndpointHarness<TEndpoint>(
    AzuriteFixture fixture,
    Action<IServiceCollection>? configureServices = null,
    IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    : IntegrationEndpointHarness(fixture, configureServices, configurationOverrides)
    where TEndpoint : class
{
    public TEndpoint Endpoint
    {
        get { return Get<TEndpoint>(); }
    }
}
