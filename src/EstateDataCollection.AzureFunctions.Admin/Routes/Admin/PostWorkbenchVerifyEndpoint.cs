using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.Admin.Helpers;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class PostWorkbenchVerifyEndpoint(
    IUseCase<VerifySchemaResult, VerifySchemaRequest> verifyUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiWorkbenchVerify";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post), Route = AppRoutes.AdminUi.Templates.WorkbenchVerify)]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, "verify");

        var form = await ReadFormSafeAsync(http, cancellationToken);
        var meta = CreatePageMeta(http, "Contract Workbench", activeNav: "workbench");

        var parseResult = WorkbenchFormParser.Parse(form, requireSchemaIdentity: false);
        if (parseResult.IsFailure)
        {
            var verifyErrorModel = new WorkbenchVerifyViewModel(HasRun: true, Error: parseResult.Error);
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

        WorkbenchVerifyViewModel verifyViewModel;
        int statusCode;

        if (verifyResult.IsFailure)
        {
            verifyViewModel = new WorkbenchVerifyViewModel(HasRun: true, Error: verifyResult.Error);
            statusCode = MapErrorToStatusCode(verifyResult.Error);
        }
        else if (!verifyResult.Value.IsSuccessful)
        {
            var validationErrors = BuildValidationErrorsFromVerifyReport(verifyResult.Value);
            var reportError = Error.Validation(
                ErrorCodes.SchemaVerificationFailed,
                verifyResult.Value.Message,
                validationErrors);
            verifyViewModel = new WorkbenchVerifyViewModel(HasRun: true, Result: verifyResult.Value, Error: reportError);
            statusCode = StatusCodes.Status422UnprocessableEntity;
        }
        else
        {
            verifyViewModel = new WorkbenchVerifyViewModel(HasRun: true, Result: verifyResult.Value);
            statusCode = StatusCodes.Status200OK;
        }

        return await RenderPageAsync<WorkbenchPage, WorkbenchViewModel>(
            EndpointName,
            input.ToViewModel(meta, verification: verifyViewModel),
            statusCode);
    }

    private static List<ValidationError> BuildValidationErrorsFromVerifyReport(VerifySchemaResult report)
    {
        var blocking = new List<ValidationError>();
        if (report.Issues is { Count: > 0 })
        {
            blocking.AddRange(ValidationIssuesHelper.GetBlockingIssues(
                report.Issues.SelectMany(kv => kv.Value),
                RuleSeverity.Error));
        }

        if (blocking.Count > 0)
        {
            return blocking;
        }

        var fallback = new List<ValidationError>();
        foreach (var err in report.SyntaxErrors ?? [])
        {
            fallback.Add(new ValidationError("schema", err, Severity: RuleSeverity.Error));
        }

        foreach (var err in report.FixtureErrors ?? [])
        {
            fallback.Add(new ValidationError("fixtures", err, Severity: RuleSeverity.Error));
        }

        return fallback;
    }
}
