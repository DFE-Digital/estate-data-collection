using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;
using EstateDataCollection.AzureFunctions.Admin.Helpers;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class PostUpdateSchemaVersionTestsEndpoint(
    IUseCase<UpdateSchemaTestsResult, UpdateSchemaTestsRequest> updateSchemaTestsUseCase,
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiUpdateSchemaVersionTests";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post),
            Route = AppRoutes.AdminUi.Templates.SchemaTests)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, $"{schemaName} v{version}");

        var form = await ReadFormSafeAsync(http, cancellationToken);
        var validRaw = form["validFixtureJson"].ToString();
        var invalidRaw = form["invalidFixtureJson"].ToString();

        var validationErrors = new List<ValidationError>();

        var validParsed = WorkbenchFormParser.ParseFixtures(validRaw, "validFixtureJson");
        if (validParsed.IsFailure)
        {
            validationErrors.AddRange(validParsed.Error.ValidationErrors);
        }

        var invalidParsed = WorkbenchFormParser.ParseFixtures(invalidRaw, "invalidFixtureJson");
        if (invalidParsed.IsFailure)
        {
            validationErrors.AddRange(invalidParsed.Error.ValidationErrors);
        }

        if (validationErrors.Count > 0)
        {
            return await RenderFailureAsync(
                Error.Validation("SchemaTests.InvalidFixtureJson", AdminUiMessages.InvalidTestJsonPayloads, validationErrors),
                StatusCodes.Status422UnprocessableEntity);
        }

        var result = await updateSchemaTestsUseCase.ExecuteAsync(
            new UpdateSchemaTestsRequest(
                SchemaName: schemaName,
                Version: version,
                ValidFixtures: validParsed.Value,
                InvalidFixtures: invalidParsed.Value),
            cancellationToken);

        if (result.IsFailure)
        {
            return await RenderFailureAsync(result.Error, MapErrorToStatusCode(result.Error));
        }

        InstrumentCompleted(EndpointName, StatusCodes.Status302Found);
        return new RedirectResult(
            AppRoutes.AdminUi.SchemaTests(schemaName, version, notice: "updated"),
            permanent: false);

        async Task<IActionResult> RenderFailureAsync(Error error, int statusCode)
        {
            var detailResult = await getSchemaDetailUseCase.ExecuteAsync(
                new GetSchemaDetailRequest(
                    SchemaName: schemaName,
                    Version: version,
                    IncludeUnscheduled: true,
                    IncludeRawJsonSchema: false,
                    IncludeUpcoming: true,
                    IncludeRetired: true),
                cancellationToken);

            var meta = CreatePageMeta(http, AdminUiMessages.TestsPageTitle(schemaName, version), activeNav: "schemas");

            var model = new SchemaTestsViewModel(
                Page: meta,
                SchemaName: detailResult.IsSuccess ? detailResult.Value.SchemaName : schemaName,
                Version: detailResult.IsSuccess ? detailResult.Value.Version : version,
                Detail: detailResult.IsSuccess ? detailResult.Value : null,
                PageError: error,
                ValidFixtureInput: validRaw,
                InvalidFixtureInput: invalidRaw);

            return await RenderPageAsync<SchemaTestsPage, SchemaTestsViewModel>(
                EndpointName,
                model,
                statusCode);
        }
    }
}
