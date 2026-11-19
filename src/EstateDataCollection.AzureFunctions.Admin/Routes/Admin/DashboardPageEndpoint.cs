using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class DashboardPageEndpoint(
    IUseCase<GetSchemasResult, GetSchemasRequest> getSchemasUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiDashboardPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get), Route = "manage")]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, "dashboard");

        var result = await getSchemasUseCase.ExecuteAsync(
            new GetSchemasRequest(
                SchemaName: null,
                IncludeUnscheduled: true,
                IncludeActiveVersionFields: false),
            cancellationToken);

        var meta = CreatePageMeta(http, "Schema Contracts & Lifecycle", activeNav: "schemas");

        var model = result.IsSuccess
            ? new DashboardViewModel(meta, result.Value.Schemas)
            : new DashboardViewModel(meta, [], PageError: result.Error);

        var statusCode = result.IsSuccess ? StatusCodes.Status200OK : MapErrorToStatusCode(result.Error);
        return await RenderPageAsync<DashboardPage, DashboardViewModel>(EndpointName, model, statusCode);
    }
}
