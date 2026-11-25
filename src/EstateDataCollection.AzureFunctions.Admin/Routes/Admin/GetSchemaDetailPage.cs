using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class GetSchemaDetailPage(
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiSchemaDetailPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get),
            Route = AppRoutes.AdminUi.Templates.SchemaDetail)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, $"{schemaName} v{version}");

        var result = await getSchemaDetailUseCase.ExecuteAsync(
            new GetSchemaDetailRequest(
                SchemaName: schemaName,
                Version: version,
                IncludeUnscheduled: true,
                IncludeRawJsonSchema: true,
                IncludeUpcoming: true,
                IncludeRetired: true),
            cancellationToken);

        var meta = CreatePageMeta(http, $"{schemaName} v{version}", activeNav: "schemas");

        if (result.IsFailure)
        {
            var errModel = new SchemaDetailViewModel(meta, Detail: null, PageError: result.Error);
            return await RenderPageAsync<SchemaDetailPage, SchemaDetailViewModel>(
                EndpointName,
                errModel,
                MapErrorToStatusCode(result.Error));
        }

        var detail = result.Value;
        var notice = http.Query["notice"].ToString();
        string? successBanner = notice switch
        {
            "submitted" => $"Schema '{detail.SchemaName}' v{detail.Version} passed verification and was saved as Validated (unscheduled).",
            "scheduled" => $"Lifecycle & schedule updated for '{detail.SchemaName}' v{detail.Version} (current state: {detail.State.Name}).",
            "signedoff" => $"Schema '{detail.SchemaName}' v{detail.Version} was signed off by '{detail.SignedOffBy}' and transitioned to Validated.",
            "deleted" => $"Schema '{detail.SchemaName}' v{detail.Version} was deleted. Metadata and error report tombstones remain reserved; any fix must be published as v{detail.Version + 1}.",
            _ => null
        };

        var model = new SchemaDetailViewModel(meta, detail, SuccessBanner: successBanner);
        return await RenderPageAsync<SchemaDetailPage, SchemaDetailViewModel>(EndpointName, model);
    }
}
