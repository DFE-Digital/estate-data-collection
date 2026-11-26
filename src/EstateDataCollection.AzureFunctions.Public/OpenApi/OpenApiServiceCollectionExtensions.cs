using System.Reflection;
using AzureFunctions.Extensions.Swashbuckle;
using AzureFunctions.Extensions.Swashbuckle.Settings;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.AzureFunctions.Public.Options;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Web.Models.Results;
using EstateDataCollection.Domain.Schema;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.Azure.Functions;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EstateDataCollection.AzureFunctions.Public.OpenApi;

public static class OpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddPublicOpenApiDocs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var docsOptions = configuration
            .GetSection(OpenApiDocsOptions.SectionName)
            .Get<OpenApiDocsOptions>() ?? new OpenApiDocsOptions();

        var specVersion = string.Equals(docsOptions.SpecVersion, "3.1", StringComparison.OrdinalIgnoreCase)
            ? OpenApiSpecVersion.OpenApi3_1
            : OpenApiSpecVersion.OpenApi3_0;

        var overviewMarkdown = OpenApiOverviewLoader.LoadOverviewMarkdown();
        var xmlDocPaths = ResolveXmlDocumentationPaths();
        var primaryAssemblyName = typeof(Program).Assembly.GetName().Name;

        services.AddSwashBuckle(opts =>
        {
            opts.RoutePrefix = "api";
            opts.SpecVersion = specVersion;
            opts.AddCodeParameter = false;
            opts.PrependOperationWithRoutePrefix = false;
            opts.Title = "Estate Data Collection Public API";
            opts.XmlPath = $"{primaryAssemblyName}.xml";
            opts.Documents =
            [
                new SwaggerDocument
                {
                    Name = "v1",
                    Title = "Estate Data Collection Public API",
                    Version = AppVersionProvider.Version,
                    Description = overviewMarkdown
                }
            ];
            opts.ConfigureSwaggerGen = swagger =>
            {
                foreach (var xmlPath in xmlDocPaths)
                {
                    var fileName = Path.GetFileNameWithoutExtension(xmlPath);
                    if (!string.Equals(fileName, primaryAssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        swagger.IncludeXmlComments(xmlPath, includeControllerXmlComments: false);
                    }
                }

                swagger.OrderActionsBy(ResolveActionSortKey);
                swagger.SchemaFilter<RecordParameterXmlDocSchemaFilter>(xmlDocPaths);
                swagger.OperationFilter<CleanFunctionsOperationFilter>(docsOptions);
                swagger.DocumentFilter<FaUApiOpenApiDocumentFilter>(docsOptions);
                swagger.CustomSchemaIds(FormatSchemaId);
            };
        }, typeof(Program).Assembly);

        services.AddScalarApiReference(scalar =>
        {
            scalar.Title = "Estate Data Collection Public API";
            scalar.RoutePrefix = "api";
            scalar.OpenApiRoutePattern = "openapi/{documentName}.json";
            scalar.HideTestRequestButton = !docsOptions.EnableTryItOut;
            scalar.DefaultOpenAllTags = true;
            scalar.SchemaPropertyOrder = PropertyOrder.Preserve;
            scalar.AddDocument("v1", title: "EDC Public API v1", routePattern: "openapi/v1.json", isDefault: true);
        });

        return services;
    }

    private static string ResolveActionSortKey(ApiDescription apiDescription)
    {
        var order = int.MaxValue;
        if (apiDescription.TryGetMethodInfo(out var methodInfo))
        {
            order = methodInfo.GetCustomAttribute<OpenApiOrderAttribute>()?.Order
                    ?? methodInfo.DeclaringType?.GetCustomAttribute<OpenApiOrderAttribute>()?.Order
                    ?? int.MaxValue;
        }

        return $"{order:D6}_{apiDescription.RelativePath}_{apiDescription.HttpMethod}";
    }

    private static IReadOnlyList<string> ResolveXmlDocumentationPaths()
    {
        Assembly[] assemblies =
        [
            typeof(Program).Assembly,
            typeof(ApiResponse<>).Assembly,
            typeof(IUseCase<,>).Assembly,
            typeof(SchemaIdentifierConfig).Assembly,
            typeof(AppVersionProvider).Assembly
        ];

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies)
        {
            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                continue;
            }

            var xmlFileName = $"{assemblyName}.xml";
            var baseDirPath = Path.Combine(AppContext.BaseDirectory, xmlFileName);
            if (File.Exists(baseDirPath) && seen.Add(baseDirPath))
            {
                paths.Add(baseDirPath);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(assembly.Location))
            {
                var locationDir = Path.GetDirectoryName(assembly.Location);
                if (!string.IsNullOrWhiteSpace(locationDir))
                {
                    var besideAssemblyPath = Path.Combine(locationDir, xmlFileName);
                    if (File.Exists(besideAssemblyPath) && seen.Add(besideAssemblyPath))
                    {
                        paths.Add(besideAssemblyPath);
                    }
                }
            }
        }

        return paths;
    }

    private static string FormatSchemaId(Type type)
    {
        if (!type.IsGenericType)
        {
            return (type.FullName?.Contains('+') == true ? type.FullName.Split('.').Last() : type.Name)
                .Replace('+', '.');
        }

        var genericName = type.Name.Split('`')[0];
        var args = string.Join("And", type.GetGenericArguments().Select(FormatSchemaId));
        return $"{genericName}Of{args}";
    }
}
