using System.Globalization;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.AzureFunctions.Admin.Helpers;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class PostScheduleSchemaSubmitEndpoint(
    IUseCase<ScheduleSchemaResult, ScheduleSchemaRequest> scheduleUseCase,
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiScheduleSchemaSubmit";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Post),
            Route = AppRoutes.AdminUi.Templates.SchemaSchedule)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, $"{schemaName} v{version}");

        var form = await ReadFormSafeAsync(http, cancellationToken);
        var lifecycleAction = form["lifecycleAction"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(lifecycleAction))
        {
            lifecycleAction = "Schedule";
        }

        var effectiveFromRaw = form["effectiveFromUtc"].ToString().Trim();
        var effectiveToRaw = form["effectiveToUtc"].ToString().Trim();
        var requestedProp = form["identifierProperty"].ToString().Trim();
        var requestedLabel = form["identifierLabel"].ToString().Trim();

        var validationErrors = new List<ValidationError>();
        var identifierConfig = WorkbenchFormParser.ParseIdentifierConfig(form, validationErrors);

        DateTimeOffset? effectiveFromUtc = null;
        DateTimeOffset? effectiveToUtc = null;

        if (!string.IsNullOrWhiteSpace(effectiveFromRaw))
        {
            if (DateTimeOffset.TryParse(
                    effectiveFromRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedFrom))
            {
                effectiveFromUtc = parsedFrom;
            }
            else
            {
                validationErrors.Add(new ValidationError(
                    "effectiveFromUtc",
                    "Effective from must be a valid UTC date/time (YYYY-MM-DDTHH:MM)."));
            }
        }

        if (!string.IsNullOrWhiteSpace(effectiveToRaw))
        {
            if (DateTimeOffset.TryParse(
                    effectiveToRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedTo))
            {
                effectiveToUtc = parsedTo;
            }
            else
            {
                validationErrors.Add(new ValidationError(
                    "effectiveToUtc",
                    "Effective to must be a valid UTC date/time (YYYY-MM-DDTHH:MM)."));
            }
        }

        if (validationErrors.Count > 0)
        {
            return await RenderFailureAsync(
                Error.Validation("Schedule.InvalidInput", "Invalid schedule input.", validationErrors),
                StatusCodes.Status422UnprocessableEntity);
        }

        var result = await scheduleUseCase.ExecuteAsync(
            new ScheduleSchemaRequest(
                SchemaName: schemaName,
                Version: version,
                EffectiveFromUtc: effectiveFromUtc,
                EffectiveToUtc: effectiveToUtc,
                LifecycleAction: lifecycleAction,
                IdentifierConfig: identifierConfig),
            cancellationToken);

        if (result.IsFailure)
        {
            return await RenderFailureAsync(result.Error, MapErrorToStatusCode(result.Error));
        }

        InstrumentCompleted(EndpointName, StatusCodes.Status302Found);
        return new RedirectResult(
            AppRoutes.AdminUi.SchemaDetail(schemaName, version, notice: "scheduled"),
            permanent: false);

        async Task<IActionResult> RenderFailureAsync(Error error, int statusCode)
        {
            var detailResult = await getSchemaDetailUseCase.ExecuteAsync(
                new GetSchemaDetailRequest(
                    SchemaName: schemaName,
                    Version: version,
                    IncludeUnscheduled: true,
                    IncludeRawJsonSchema: false),
                cancellationToken);

            var meta = CreatePageMeta(http, $"Schedule {schemaName} v{version}", activeNav: "schemas");
            var availableProperties = detailResult.IsSuccess
                ? IdentifierPropertyDiscovery.FromFieldDescriptors(detailResult.Value.Fields)
                : [];
            var (mode, selectedProperty, pointer, label) = ParsedWorkbenchInput.ResolveUiSelection(
                identifierConfig,
                availableProperties,
                string.IsNullOrWhiteSpace(requestedProp) ? null : requestedProp,
                string.IsNullOrWhiteSpace(requestedLabel) ? null : requestedLabel);

            var model = new ScheduleSchemaViewModel(
                Page: meta,
                SchemaName: schemaName,
                Version: version,
                Detail: detailResult.IsSuccess ? detailResult.Value : null,
                SelectedAction: lifecycleAction,
                EffectiveFromInput: effectiveFromRaw,
                EffectiveToInput: effectiveToRaw,
                IdentifierMode: mode,
                IdentifierProperty: selectedProperty,
                IdentifierPointer: pointer,
                IdentifierLabel: label,
                IdentifierRequired: detailResult.IsSuccess ? detailResult.Value.IdentifierConfig?.Required : null,
                AvailableIdentifierProperties: availableProperties,
                PageError: error);

            return await RenderPageAsync<ScheduleSchemaPage, ScheduleSchemaViewModel>(
                EndpointName,
                model,
                statusCode);
        }
    }
}
