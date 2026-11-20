using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class GetStatisticsPage(
    IUseCase<GetSubmissionStatisticsResult, GetSubmissionStatisticsRequest> getStatisticsUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiStatisticsPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get), Route = AppRoutes.AdminUi.Templates.Statistics)]
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        var schemaName = http.Query.TryGetValue("schemaName", out var rawSchema) && !string.IsNullOrWhiteSpace(rawSchema)
            ? rawSchema.ToString().Trim()
            : null;

        InstrumentInvoked(EndpointName, schemaName ?? "all");

        var result = await getStatisticsUseCase.ExecuteAsync(
            new GetSubmissionStatisticsRequest(schemaName),
            cancellationToken);

        var meta = CreatePageMeta(http, "Submission Statistics", activeNav: "statistics");

        if (result.IsFailure)
        {
            var errModel = new StatisticsViewModel(meta, Statistics: null, PageError: result.Error);
            return await RenderPageAsync<StatisticsPage, StatisticsViewModel>(
                EndpointName,
                errModel,
                MapErrorToStatusCode(result.Error));
        }

        var model = new StatisticsViewModel(meta, result.Value);
        return await RenderPageAsync<StatisticsPage, StatisticsViewModel>(EndpointName, model);
    }
}
