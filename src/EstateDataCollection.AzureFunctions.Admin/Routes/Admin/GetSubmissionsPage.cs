using System.Globalization;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissions;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class GetSubmissionsPage(
    IUseCase<GetSubmissionsResult, GetSubmissionsRequest> getSubmissionsUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiSubmissionsPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get), Route = AppRoutes.AdminUi.Templates.Submissions)]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        var schemaName = http.Query.TryGetValue("schemaName", out var rawSchema) && !string.IsNullOrWhiteSpace(rawSchema)
            ? rawSchema.ToString().Trim()
            : null;

        int? schemaVersion = null;
        if (http.Query.TryGetValue("version", out var rawVersion) &&
            int.TryParse(rawVersion.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVersion))
        {
            schemaVersion = parsedVersion;
        }

        var status = http.Query.TryGetValue("status", out var rawStatus) && !string.IsNullOrWhiteSpace(rawStatus)
            ? rawStatus.ToString().Trim()
            : null;

        var query = http.Query.TryGetValue("q", out var rawQuery) && !string.IsNullOrWhiteSpace(rawQuery)
            ? rawQuery.ToString().Trim()
            : null;

        InstrumentInvoked(EndpointName, schemaName ?? "all");

        var result = await getSubmissionsUseCase.ExecuteAsync(
            new GetSubmissionsRequest(schemaName, schemaVersion, status, query),
            cancellationToken);

        var meta = CreatePageMeta(http, "Submissions", activeNav: "submissions");

        if (result.IsFailure)
        {
            var errModel = new SubmissionsViewModel(meta, Result: null, PageError: result.Error);
            return await RenderPageAsync<SubmissionsPage, SubmissionsViewModel>(
                EndpointName,
                errModel,
                MapErrorToStatusCode(result.Error));
        }

        var model = new SubmissionsViewModel(meta, result.Value);
        return await RenderPageAsync<SubmissionsPage, SubmissionsViewModel>(EndpointName, model);
    }
}
