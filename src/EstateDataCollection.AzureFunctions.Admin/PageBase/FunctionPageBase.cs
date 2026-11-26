using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.AzureFunctions.Admin.Security;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Security;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.AzureFunctions.Admin.PageBase;

[RequireRoles(AdminRoleKeys.Operator)]
public abstract class FunctionPageBase(
    RazorComponentRenderer renderer,
    FunctionEndpointInstrumentation? instrumentation = null
) : IFunctionEndpoint
{
    private readonly FunctionEndpointInstrumentation _instrumentation =
        instrumentation ?? new FunctionEndpointInstrumentation(NullLogger<FunctionEndpointInstrumentation>.Instance);

    protected void InstrumentInvoked(string endpointName, string context = "ui")
    {
        _instrumentation.EndpointInvoked(endpointName, context);
    }

    protected void InstrumentCompleted(string endpointName, int statusCode)
    {
        _instrumentation.EndpointCompleted(endpointName, statusCode);
    }

    protected AdminPageMeta CreatePageMeta(
        HttpRequest http,
        string title,
        string activeNav = "schemas")
    {
        var userContext = http.HttpContext?.RequestServices?.GetService<IUserContext>();
        var signedInUser = userContext is { IsAuthenticated: true }
            ? userContext.DisplayName ?? userContext.Email ?? userContext.UserId
            : EasyAuthPrincipalParser.ResolveDisplayName(http);

        return new AdminPageMeta(
            Title: title,
            ActiveNav: activeNav,
            SignedInUser: signedInUser,
            AppVersion: AppVersionProvider.Version);
    }

    protected async Task<IActionResult> RenderPageAsync<TComponent, TViewModel>(
        string endpointName,
        TViewModel model,
        int statusCode = StatusCodes.Status200OK)
        where TComponent : IComponent
    {
        var html = await renderer.RenderComponentAsync<TComponent>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });

        _instrumentation.EndpointCompleted(endpointName, statusCode);

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = statusCode
        };
    }

    protected static int MapErrorToStatusCode(Error error)
    {
        return error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };
    }

    protected static async Task<IFormCollection> ReadFormSafeAsync(
        HttpRequest http,
        CancellationToken cancellationToken)
    {
        if (http.HasFormContentType)
        {
            return await http.ReadFormAsync(cancellationToken);
        }

        return FormCollection.Empty;
    }
}
