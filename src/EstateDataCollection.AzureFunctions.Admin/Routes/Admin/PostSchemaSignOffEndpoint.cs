using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.SignOffSchema;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class PostSchemaSignOffEndpoint(
    IUseCase<SignOffSchemaResult, SignOffSchemaRequest> signOffUseCase,
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiSchemaSignOff";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post),
            Route = AppRoutes.AdminUi.Templates.SchemaSignOff)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, $"{schemaName} v{version}");

        var form = await ReadFormSafeAsync(http, cancellationToken);
        var signedOffBy = form["signedOffBy"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(signedOffBy))
        {
            signedOffBy = "admin-ui";
        }

        var result = await signOffUseCase.ExecuteAsync(
            new SignOffSchemaRequest(schemaName, version, signedOffBy),
            cancellationToken);

        if (result.IsFailure)
        {
            var statusCode = MapErrorToStatusCode(result.Error);
            var detailResult = await getSchemaDetailUseCase.ExecuteAsync(
                new GetSchemaDetailRequest(
                    SchemaName: schemaName,
                    Version: version,
                    IncludeUnscheduled: true,
                    IncludeUpcoming: true,
                    IncludeRetired: true,
                    IncludeRawJsonSchema: true),
                cancellationToken);

            var meta = CreatePageMeta(
                http,
                $"{schemaName} v{version}",
                activeNav: "schemas");

            return await RenderPageAsync<SchemaDetailPage, SchemaDetailViewModel>(
                EndpointName,
                new SchemaDetailViewModel(
                    meta,
                    Detail: detailResult.IsSuccess ? detailResult.Value : null,
                    PageError: result.Error),
                statusCode);
        }

        InstrumentCompleted(EndpointName, StatusCodes.Status302Found);
        return new RedirectResult(
            AppRoutes.AdminUi.SchemaDetail(schemaName, version, notice: "signedoff"),
            permanent: false);
    }
}
