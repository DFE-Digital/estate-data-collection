using System.Text;
using System.Text.Json;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.AzureFunctions.Admin.PageBase;

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
        return new AdminPageMeta(
            Title: title,
            ActiveNav: activeNav,
            SignedInUser: ResolveSignedInUser(http),
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

    private static string? ResolveSignedInUser(HttpRequest http)
    {
        if (http.Headers.TryGetValue("X-MS-CLIENT-PRINCIPAL-NAME", out var principalName) &&
            !string.IsNullOrWhiteSpace(principalName))
        {
            return principalName.ToString().Trim();
        }

        if (http.Headers.TryGetValue("X-MS-CLIENT-PRINCIPAL", out var encodedPrincipal) &&
            !string.IsNullOrWhiteSpace(encodedPrincipal))
        {
            var decoded = TryDecodeEasyAuthPrincipal(encodedPrincipal.ToString());
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                return decoded;
            }
        }

        var identityName = http.HttpContext?.User?.Identity?.Name;
        return string.IsNullOrWhiteSpace(identityName) ? null : identityName;
    }

    private static string? TryDecodeEasyAuthPrincipal(string encoded)
    {
        try
        {
            var bytes = Convert.FromBase64String(encoded.Trim());
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            var root = doc.RootElement;

            if (root.TryGetProperty("userDetails", out var userDetails) &&
                userDetails.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(userDetails.GetString()))
            {
                return userDetails.GetString();
            }

            if (root.TryGetProperty("claims", out var claims) && claims.ValueKind == JsonValueKind.Array)
            {
                foreach (var claim in claims.EnumerateArray())
                {
                    var typ = claim.TryGetProperty("typ", out var typProp) ? typProp.GetString() : null;
                    var val = claim.TryGetProperty("val", out var valProp) ? valProp.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(val) &&
                        (string.Equals(typ, "name", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(typ, "preferred_username", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(typ, "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", StringComparison.OrdinalIgnoreCase)))
                    {
                        return val;
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed client principal header
        }

        return null;
    }
}
