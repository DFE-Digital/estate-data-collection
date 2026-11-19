using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes;

public sealed class SchemaTestsPageEndpoint(
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiSchemaTestsPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get),
            Route = "manage/schemas/{schemaName}/{version:int}/tests")]
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
                IncludeRawJsonSchema: false),
            cancellationToken);

        var meta = CreatePageMeta(http, $"Tests - {schemaName} v{version}", activeNav: "schemas");

        if (result.IsFailure)
        {
            var errModel = new SchemaTestsViewModel(
                Page: meta,
                SchemaName: schemaName,
                Version: version,
                Detail: null,
                PageError: result.Error);

            return await RenderPageAsync<SchemaTestsPage, SchemaTestsViewModel>(
                EndpointName,
                errModel,
                MapErrorToStatusCode(result.Error));
        }

        var detail = result.Value;
        var notice = http.Query["notice"].ToString();
        string? successBanner = notice switch
        {
            "updated" => $"Stored regression test payloads verified and saved for '{detail.SchemaName}' v{detail.Version}.",
            _ => null
        };

        var model = new SchemaTestsViewModel(
            Page: meta,
            SchemaName: detail.SchemaName,
            Version: detail.Version,
            Detail: detail,
            SuccessBanner: successBanner);

        return await RenderPageAsync<SchemaTestsPage, SchemaTestsViewModel>(EndpointName, model);
    }
}
