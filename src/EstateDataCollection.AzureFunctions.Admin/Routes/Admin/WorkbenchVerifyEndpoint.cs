using EstateDataCollection.Admin.UI.Components;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.Admin.Helpers;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class WorkbenchVerifyEndpoint(
    IUseCase<VerifySchemaResult, VerifySchemaRequest> verifyUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiWorkbenchVerify";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post), Route = "manage/workbench/verify")]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, "verify");

        var form = await ReadFormSafeAsync(http, cancellationToken);
        var isFetch = string.Equals(http.Headers["X-Requested-With"], "fetch", StringComparison.OrdinalIgnoreCase);
        var meta = CreatePageMeta(http, "Contract Workbench", activeNav: "workbench");

        var parseResult = WorkbenchFormParser.Parse(form, requireSchemaIdentity: false);
        if (parseResult.IsFailure)
        {
            var verifyErrorModel = new WorkbenchVerifyViewModel(HasRun: true, Error: parseResult.Error);
            if (isFetch)
            {
                return await RenderPartialAsync<VerifyResultPanel>(
                    EndpointName,
                    new Dictionary<string, object?> { ["Model"] = verifyErrorModel },
                    StatusCodes.Status422UnprocessableEntity);
            }

            var lenient = WorkbenchFormParser.ParseLenientForRedisplay(form);
            return await RenderPageAsync<WorkbenchPage, WorkbenchViewModel>(
                EndpointName,
                lenient.ToViewModel(meta, verification: verifyErrorModel),
                StatusCodes.Status422UnprocessableEntity);
        }

        var input = parseResult.Value;
        var verifyResult = await verifyUseCase.ExecuteAsync(
            new VerifySchemaRequest(
                SchemaJson: input.InputMode == "json" ? input.SchemaJson : null,
                SampleValidFixtures: input.ValidFixtures,
                SampleInvalidFixtures: input.InvalidFixtures,
                RulesJson: input.InputMode == "json" ? input.RulesJson : null,
                OdcsYaml: input.InputMode == "odcs" ? input.OdcsYaml : null,
                FileName: string.IsNullOrWhiteSpace(input.FileName) ? null : input.FileName,
                IdentifierConfig: input.IdentifierConfig),
            cancellationToken);

        var verifyViewModel = verifyResult.IsSuccess
            ? new WorkbenchVerifyViewModel(HasRun: true, Result: verifyResult.Value)
            : new WorkbenchVerifyViewModel(HasRun: true, Error: verifyResult.Error);

        var statusCode = verifyResult.IsSuccess
            ? StatusCodes.Status200OK
            : MapErrorToStatusCode(verifyResult.Error);

        if (isFetch)
        {
            return await RenderPartialAsync<VerifyResultPanel>(
                EndpointName,
                new Dictionary<string, object?> { ["Model"] = verifyViewModel },
                statusCode);
        }

        return await RenderPageAsync<WorkbenchPage, WorkbenchViewModel>(
            EndpointName,
            input.ToViewModel(meta, verification: verifyViewModel),
            statusCode);
    }
}
