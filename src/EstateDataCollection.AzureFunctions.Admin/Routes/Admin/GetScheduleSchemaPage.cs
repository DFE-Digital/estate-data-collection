using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.AzureFunctions.Admin.Helpers;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class GetScheduleSchemaPage(
    IUseCase<SchemaVersionDetail, GetSchemaDetailRequest> getSchemaDetailUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiScheduleSchemaPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get),
            Route = AppRoutes.AdminUi.Templates.SchemaSchedule)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, $"{schemaName} v{version}");

        var detailResult = await getSchemaDetailUseCase.ExecuteAsync(
            new GetSchemaDetailRequest(
                SchemaName: schemaName,
                Version: version,
                IncludeUnscheduled: true,
                IncludeRawJsonSchema: false,
                IncludeUpcoming: true,
                IncludeRetired: true),
            cancellationToken);

        var meta = CreatePageMeta(http, AdminUiMessages.ScheduleSchemaTitle(schemaName, version), activeNav: "schemas");

        if (detailResult.IsFailure)
        {
            var errModel = new ScheduleSchemaViewModel(
                Page: meta,
                SchemaName: schemaName,
                Version: version,
                Detail: null,
                PageError: detailResult.Error);

            return await RenderPageAsync<ScheduleSchemaPage, ScheduleSchemaViewModel>(
                EndpointName,
                errModel,
                MapErrorToStatusCode(detailResult.Error));
        }

        var detail = detailResult.Value;
        var selectedAction = detail.State.Name switch
        {
            "Deprecated" => "Deprecate",
            "Active" => "Activate",
            _ => "Schedule"
        };

        var availableProperties = IdentifierPropertyDiscovery.FromFieldDescriptors(detail.Fields);
        var (mode, selectedProperty, pointer, label) = ParsedWorkbenchInput.ResolveUiSelection(
            detail.IdentifierConfig,
            availableProperties);

        bool? effectiveRequired = detail.IdentifierConfig?.Required;
        if (effectiveRequired is null && mode != "none")
        {
            var matched = availableProperties.FirstOrDefault(p =>
                string.Equals(p.Pointer, selectedProperty, StringComparison.OrdinalIgnoreCase));
            effectiveRequired = matched?.Required;
        }

        var model = new ScheduleSchemaViewModel(
            Page: meta,
            SchemaName: detail.SchemaName,
            Version: detail.Version,
            Detail: detail,
            SelectedAction: selectedAction,
            EffectiveFromInput: detail.EffectiveFromUtc?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm") ?? string.Empty,
            EffectiveToInput: detail.EffectiveToUtc?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm") ?? string.Empty,
            IdentifierMode: mode,
            IdentifierProperty: selectedProperty,
            IdentifierPointer: pointer,
            IdentifierLabel: label,
            IdentifierRequired: effectiveRequired,
            AvailableIdentifierProperties: availableProperties);

        return await RenderPageAsync<ScheduleSchemaPage, ScheduleSchemaViewModel>(EndpointName, model);
    }
}
