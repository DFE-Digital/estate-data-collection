using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.AzureFunctions.AdminUi.Options;
using EstateDataCollection.AzureFunctions.AdminUi.PageBase;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.AzureFunctions.AdminUi.Routes;

public sealed class RunAllSchemaTestsEndpoint(
    IUseCase<RunSchemaTestsResult, RunSchemaTestsRequest> runSchemaTestsUseCase,
    IUseCase<GetSchemasResult, GetSchemasRequest> getSchemasUseCase,
    RazorComponentRenderer renderer,
    IOptions<AdminUiOptions> uiOptions,
    IHostEnvironment hostEnvironment,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, uiOptions, hostEnvironment, instrumentation)
{
    public const string EndpointName = "AdminUiRunAllSchemaTests";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post),
            Route = "manage/tests/run")]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, "all-schemas");

        var runResult = await runSchemaTestsUseCase.ExecuteAsync(
            new RunSchemaTestsRequest(SchemaName: null, Version: null, IncludeRetired: false),
            cancellationToken);

        var schemasResult = await getSchemasUseCase.ExecuteAsync(
            new GetSchemasRequest(SchemaName: null, IncludeUnscheduled: true, IncludeActiveVersionFields: false),
            cancellationToken);

        var meta = CreatePageMeta(http, "Schema Contracts & Lifecycle", activeNav: "schemas");

        if (runResult.IsFailure)
        {
            var errModel = new DashboardViewModel(
                Page: meta,
                Schemas: schemasResult.IsSuccess ? schemasResult.Value.Schemas : [],
                PageError: runResult.Error);

            return await RenderPageAsync<DashboardPage, DashboardViewModel>(
                EndpointName,
                errModel,
                MapErrorToStatusCode(runResult.Error));
        }

        var run = runResult.Value;
        string? banner = run.FailedCount == 0
            ? $"All {run.PassedCount} schema(s) passed regression tests on build {run.AppVersion}."
            : null;

        var model = new DashboardViewModel(
            Page: meta,
            Schemas: schemasResult.IsSuccess ? schemasResult.Value.Schemas : [],
            SuccessBanner: banner,
            TestRunResult: run);

        return await RenderPageAsync<DashboardPage, DashboardViewModel>(EndpointName, model);
    }
}
