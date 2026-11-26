using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionDetail;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class GetSubmissionDetailPage(
    IUseCase<GetSubmissionDetailResult, GetSubmissionDetailRequest> getSubmissionDetailUseCase,
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiSubmissionDetailPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get), Route = AppRoutes.AdminUi.Templates.SubmissionDetail)]
        HttpRequest http,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        InstrumentInvoked(EndpointName, submissionId.ToString());

        var result = await getSubmissionDetailUseCase.ExecuteAsync(
            new GetSubmissionDetailRequest(submissionId),
            cancellationToken);

        var meta = CreatePageMeta(http, AdminUiMessages.SubmissionDetailTitle(submissionId), activeNav: "submissions");

        if (result.IsFailure)
        {
            var errModel = new SubmissionDetailViewModel(meta, Detail: null, PageError: result.Error);
            return await RenderPageAsync<SubmissionDetailPage, SubmissionDetailViewModel>(
                EndpointName,
                errModel,
                MapErrorToStatusCode(result.Error));
        }

        var model = new SubmissionDetailViewModel(meta, result.Value);
        return await RenderPageAsync<SubmissionDetailPage, SubmissionDetailViewModel>(EndpointName, model);
    }
}
