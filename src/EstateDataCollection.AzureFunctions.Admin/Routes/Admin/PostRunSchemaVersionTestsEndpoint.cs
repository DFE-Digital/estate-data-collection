using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class PostRunSchemaVersionTestsEndpoint(
    IUseCase<RunSchemaTestsResult, RunSchemaTestsRequest> runSchemaTestsUseCase,
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiRunSchemaVersionTests";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post),
            Route = "manage/schemas/{schemaName}/{version:int}/tests/run")]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, $"{schemaName} v{version}");

        var runResult = await runSchemaTestsUseCase.ExecuteAsync(
            new RunSchemaTestsRequest(SchemaName: schemaName, Version: version, IncludeRetired: true),
            cancellationToken);

        var detailResult = await getSchemaDetailUseCase.ExecuteAsync(
            new GetSchemaDetailRequest(
                SchemaName: schemaName,
                Version: version,
                IncludeUnscheduled: true,
                IncludeRawJsonSchema: false),
            cancellationToken);

        var meta = CreatePageMeta(http, $"Tests - {schemaName} v{version}", activeNav: "schemas");

        if (runResult.IsFailure)
        {
            var errModel = new SchemaTestsViewModel(
                Page: meta,
                SchemaName: schemaName,
                Version: version,
                Detail: detailResult.IsSuccess ? detailResult.Value : null,
                PageError: runResult.Error);

            return await RenderPageAsync<SchemaTestsPage, SchemaTestsViewModel>(
                EndpointName,
                errModel,
                MapErrorToStatusCode(runResult.Error));
        }

        var model = new SchemaTestsViewModel(
            Page: meta,
            SchemaName: detailResult.IsSuccess ? detailResult.Value.SchemaName : schemaName,
            Version: detailResult.IsSuccess ? detailResult.Value.Version : version,
            Detail: detailResult.IsSuccess ? detailResult.Value : null,
            LastRunResult: runResult.Value);

        return await RenderPageAsync<SchemaTestsPage, SchemaTestsViewModel>(EndpointName, model);
    }
}
