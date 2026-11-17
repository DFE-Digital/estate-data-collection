using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.AzureFunctions.AdminUi.Options;
using EstateDataCollection.AzureFunctions.AdminUi.PageBase;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.AzureFunctions.AdminUi.Routes;

public sealed class SchemaVersionsPageEndpoint(
    IUseCase<GetSchemasResult, GetSchemasRequest> getSchemasUseCase,
    RazorComponentRenderer renderer,
    IOptions<AdminUiOptions> uiOptions,
    IHostEnvironment hostEnvironment,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, uiOptions, hostEnvironment, instrumentation)
{
    public const string EndpointName = "AdminUiSchemaVesionsPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get),
            Route = "manage/schemas/{schemaName}/")]
        HttpRequest http,
        string schemaName,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, schemaName);

        var result = await getSchemasUseCase.ExecuteAsync(
            new GetSchemasRequest(
                SchemaName: schemaName,
                IncludeUnscheduled: true),
            cancellationToken);

        var meta = CreatePageMeta(http, schemaName, activeNav: "schemas");

        if (result.IsFailure)
        {
            var errModel = new SchemaVersionsViewModel(meta, Schema: null, PageError: result.Error);
            return await RenderPageAsync<SchemaVersionsPage, SchemaVersionsViewModel>(
                EndpointName,
                errModel,
                MapErrorToStatusCode(result.Error));
        }

        var model = new SchemaVersionsViewModel(meta, result.Value.Schemas.First());
        return await RenderPageAsync<SchemaVersionsPage, SchemaVersionsViewModel>(EndpointName, model);
    }
}
