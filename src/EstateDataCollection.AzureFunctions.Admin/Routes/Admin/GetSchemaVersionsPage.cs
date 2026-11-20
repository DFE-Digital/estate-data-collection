using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class GetSchemaVersionsPage(
    IUseCase<GetSchemasResult, GetSchemasRequest> getSchemasUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiSchemaVersionsPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get),
            Route = AppRoutes.AdminUi.Templates.SchemaVersions)]
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
