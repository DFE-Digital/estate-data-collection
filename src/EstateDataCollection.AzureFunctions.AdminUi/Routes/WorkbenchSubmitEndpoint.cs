using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.AzureFunctions.AdminUi.Helpers;
using EstateDataCollection.AzureFunctions.AdminUi.Options;
using EstateDataCollection.AzureFunctions.AdminUi.PageBase;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.AzureFunctions.AdminUi.Routes;

public sealed class WorkbenchSubmitEndpoint(
    IUseCase<SubmitSchemaResult, SubmitSchemaRequest> submitUseCase,
    RazorComponentRenderer renderer,
    IOptions<AdminUiOptions> uiOptions,
    IHostEnvironment hostEnvironment,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, uiOptions, hostEnvironment, instrumentation)
{
    public const string EndpointName = "AdminUiWorkbenchSubmit";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post), Route = "manage/workbench/submit")]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, "submit");

        var form = await ReadFormSafeAsync(http, cancellationToken);
        var meta = CreatePageMeta(http, "Contract Workbench", activeNav: "workbench");

        var parseResult = WorkbenchFormParser.Parse(form, requireSchemaIdentity: true);
        if (parseResult.IsFailure)
        {
            var lenient = WorkbenchFormParser.ParseLenientForRedisplay(form);
            return await RenderPageAsync<WorkbenchPage, WorkbenchViewModel>(
                EndpointName,
                lenient.ToViewModel(meta, pageError: parseResult.Error),
                StatusCodes.Status422UnprocessableEntity);
        }

        var input = parseResult.Value;
        var submitResult = await submitUseCase.ExecuteAsync(
            new SubmitSchemaRequest(
                SchemaName: input.SchemaName,
                Version: input.Version,
                SchemaJson: input.InputMode == "json" ? input.SchemaJson : string.Empty,
                SampleValidFixtures: input.ValidFixtures,
                SampleInvalidFixtures: input.InvalidFixtures,
                RulesJson: input.InputMode == "json" ? input.RulesJson : null,
                OdcsYaml: input.InputMode == "odcs" ? input.OdcsYaml : null,
                FileName: string.IsNullOrWhiteSpace(input.FileName) ? null : input.FileName,
                IdentifierConfig: input.IdentifierConfig),
            cancellationToken);

        if (submitResult.IsFailure)
        {
            return await RenderPageAsync<WorkbenchPage, WorkbenchViewModel>(
                EndpointName,
                input.ToViewModel(meta, pageError: submitResult.Error),
                MapErrorToStatusCode(submitResult.Error));
        }

        InstrumentCompleted(EndpointName, StatusCodes.Status302Found);
        return new RedirectResult(
            $"/manage/schemas/{Uri.EscapeDataString(submitResult.Value.SchemaName)}/{submitResult.Value.Version}/details?notice=submitted",
            permanent: false);
    }
}
