using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.AzureFunctions.Admin.PageBase;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Admin;

public sealed class GetWorkbenchPage(
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionPageBase(renderer, instrumentation)
{
    public const string EndpointName = "AdminUiWorkbenchPage";

    [Function(EndpointName)]
    public async Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, nameof(HttpMethods.Get), Route = AppRoutes.AdminUi.Templates.Workbench)]
        HttpRequest http)
    {
        InstrumentInvoked(EndpointName, "workbench");

        var meta = CreatePageMeta(http, "Contract Workbench", activeNav: "workbench");
        var model = new WorkbenchViewModel(meta);

        return await RenderPageAsync<WorkbenchPage, WorkbenchViewModel>(EndpointName, model);
    }
}
