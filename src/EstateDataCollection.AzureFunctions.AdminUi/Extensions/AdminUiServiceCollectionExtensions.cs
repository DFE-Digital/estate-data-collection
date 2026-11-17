using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.AdminUi.Options;
using EstateDataCollection.AzureFunctions.AdminUi.Proxies;
using EstateDataCollection.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.AzureFunctions.AdminUi.Extensions;

public static class AdminUiServiceCollectionExtensions
{
    public static IServiceCollection AddAdminUiUseCases(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<RazorComponentRenderer>();

        var executionMode = configuration[$"{AdminUiOptions.SectionName}:{nameof(AdminUiOptions.ExecutionMode)}"];
        if (string.Equals(executionMode?.Trim(), AdminUiOptions.HttpProxyMode, StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient(AdminApiHttpClientNames.AdminApi, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AdminUiOptions>>().Value;
                var baseUrl = string.IsNullOrWhiteSpace(options.AdminApiBaseUrl)
                    ? "http://localhost:7072/api/"
                    : options.AdminApiBaseUrl.TrimEnd('/') + "/";

                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);

                if (!string.IsNullOrWhiteSpace(options.AdminApiFunctionKey))
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation(
                        "x-functions-key",
                        options.AdminApiFunctionKey.Trim());
                }
            });

            services.AddScoped<IUseCase<GetSchemasResult, GetSchemasRequest>, HttpGetSchemasUseCaseProxy>();
            services.AddScoped<IUseCase<SchemaVersionDetail, GetSchemaDetailRequest>, HttpGetSchemaDetailUseCaseProxy>();
            services.AddScoped<IUseCase<VerifySchemaResult, VerifySchemaRequest>, HttpVerifySchemaUseCaseProxy>();
            services.AddScoped<IUseCase<SubmitSchemaResult, SubmitSchemaRequest>, HttpSubmitSchemaUseCaseProxy>();
            services.AddScoped<IUseCase<ScheduleSchemaResult, ScheduleSchemaRequest>, HttpScheduleSchemaUseCaseProxy>();
            services.AddScoped<IUseCase<RunSchemaTestsResult, RunSchemaTestsRequest>, HttpRunSchemaTestsUseCaseProxy>();
            services.AddScoped<IUseCase<UpdateSchemaTestsResult, UpdateSchemaTestsRequest>, HttpUpdateSchemaTestsUseCaseProxy>();
        }
        else
        {
            services.AddApplication();
            services.AddInfrastructure(configuration);
        }

        return services;
    }
}
