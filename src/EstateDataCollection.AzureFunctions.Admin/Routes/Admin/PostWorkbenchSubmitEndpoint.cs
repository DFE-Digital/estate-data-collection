using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.AzureFunctions.Admin.Helpers;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class PostWorkbenchSubmitEndpoint(
    IUseCase<SubmitSchemaResult, SubmitSchemaRequest> submitUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiWorkbenchSubmit";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post), Route = AppRoutes.AdminUi.Templates.WorkbenchSubmit)]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, "submit");

        var form = await ReadFormSafeAsync(http, cancellationToken);
        var meta = CreatePageMeta(http, AdminUiMessages.WorkbenchTitle, activeNav: "workbench");

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
            AppRoutes.AdminUi.SchemaDetail(
                submitResult.Value.SchemaName,
                submitResult.Value.Version,
                notice: "submitted"),
            permanent: false);
    }
}
