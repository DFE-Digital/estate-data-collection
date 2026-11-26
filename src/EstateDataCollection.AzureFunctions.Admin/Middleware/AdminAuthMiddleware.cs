using System.Diagnostics;
using System.Security.Claims;
using EstateDataCollection.AzureFunctions.Admin.Options;
using EstateDataCollection.AzureFunctions.Admin.Security;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Security;
using EstateDataCollection.Common.Web.Models.Results;
using EstateDataCollection.Common.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.AzureFunctions.Admin.Middleware;

/// <summary>
/// Enforces attribute-driven Microsoft Entra ID authentication (<c>[AllowAnonymous]</c> /
/// <see cref="RequireRolesAttribute"/>) on the consolidated Admin Function App when Easy Auth is enabled,
/// and populates scoped <see cref="IUserContext"/> from Entra ID credentials.
/// Function-Key authentication for <c>/api/*</c> remains solely the responsibility of the Azure Functions host
/// via <c>[HttpTrigger(AuthorizationLevel.Function)]</c>.
/// </summary>
public sealed class AdminAuthMiddleware(
    IOptions<AdminAuthOptions> options,
    ILogger<AdminAuthMiddleware> logger,
    IFunctionAuthMetadataResolver? metadataResolver = null
) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext is null)
        {
            await next(context);
            return;
        }

        var userContext = context.InstanceServices.GetService<UserContext>();
        var metadata = metadataResolver?.Resolve(context);
        await InvokeHttpAsync(httpContext, () => next(context), userContext, metadata);
    }

    public async Task InvokeHttpAsync(
        HttpContext httpContext,
        Func<Task> next,
        UserContext? userContext = null,
        EndpointAuthMetadata? metadata = null)
    {
        var resolvedUserContext = userContext ?? httpContext.RequestServices?.GetService<UserContext>();
        PopulateUserFromEasyAuth(httpContext, resolvedUserContext);

        var authOptions = options.Value;
        if (!authOptions.Enabled)
        {
            await ExecuteWithUserTelemetryAsync(resolvedUserContext, next);
            return;
        }

        var authMetadata = metadata ?? EndpointAuthMetadata.DefaultProtected;
        if (authMetadata.AllowAnonymous)
        {
            await ExecuteWithUserTelemetryAsync(resolvedUserContext, next);
            return;
        }

        var isApiRequest = IsApiRequest(httpContext);
        if (!await AuthorisePrincipalAsync(httpContext, authOptions, authMetadata, isApiRequest))
        {
            return;
        }

        await ExecuteWithUserTelemetryAsync(resolvedUserContext, next);
    }

    private async Task<bool> AuthorisePrincipalAsync(
        HttpContext httpContext,
        AdminAuthOptions authOptions,
        EndpointAuthMetadata authMetadata,
        bool isApiRequest)
    {
        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            if (!isApiRequest &&
                (HttpMethods.IsGet(httpContext.Request.Method) || HttpMethods.IsHead(httpContext.Request.Method)))
            {
                var returnUrl = $"{httpContext.Request.Path}{httpContext.Request.QueryString}";
                var loginBase = string.IsNullOrWhiteSpace(authOptions.LoginPath)
                    ? "/.auth/login/aad"
                    : authOptions.LoginPath.TrimEnd('/');
                var redirectUrl = string.IsNullOrEmpty(returnUrl)
                    ? loginBase
                    : $"{loginBase}?post_login_redirect_uri={Uri.EscapeDataString(returnUrl)}";

                httpContext.Response.Redirect(redirectUrl);
                return false;
            }

            if (isApiRequest)
            {
                await WriteApiProblemAsync(
                    httpContext,
                    StatusCodes.Status401Unauthorized,
                    "Auth.Unauthorized",
                    "A valid Microsoft Entra ID credential is required.");
            }
            else
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }

            return false;
        }

        if (!SatisfiesAllRoleRequirements(user, authOptions, authMetadata, out var missingRequirement))
        {
            logger.LogWarning(
                "Authenticated caller {User} lacks required Entra ID role(s) [{Roles}] on {Path}",
                user.Identity?.Name ?? "(unknown)",
                missingRequirement,
                httpContext.Request.Path);

            if (isApiRequest)
            {
                await WriteApiProblemAsync(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    "Auth.Forbidden",
                    "Caller lacks the required Entra ID role.");
            }
            else
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsync(
                    "Access denied. Your Microsoft Entra ID account does not hold the required Admin role.",
                    httpContext.RequestAborted);
            }

            return false;
        }

        return true;
    }

    private static bool SatisfiesAllRoleRequirements(
        ClaimsPrincipal user,
        AdminAuthOptions authOptions,
        EndpointAuthMetadata metadata,
        out string missingRequirement)
    {
        foreach (var requirementGroup in metadata.RoleRequirements)
        {
            var groupSatisfied = false;
            var expandedDisplay = new List<string>();

            foreach (var roleOrKey in requirementGroup)
            {
                var (allowAnyAuthenticated, acceptedRoles) = authOptions.ResolveAcceptedRoles(roleOrKey);
                if (allowAnyAuthenticated || EasyAuthPrincipalParser.HasAnyRole(user, acceptedRoles))
                {
                    groupSatisfied = true;
                    break;
                }

                expandedDisplay.AddRange(acceptedRoles);
            }

            if (!groupSatisfied)
            {
                missingRequirement = string.Join(", ", expandedDisplay);
                return false;
            }
        }

        missingRequirement = string.Empty;
        return true;
    }

    private async Task ExecuteWithUserTelemetryAsync(UserContext? userContext, Func<Task> next)
    {
        if (userContext is not { IsAuthenticated: true })
        {
            await next();
            return;
        }

        var activity = Activity.Current;
        if (activity is not null)
        {
            if (!string.IsNullOrWhiteSpace(userContext.UserId))
            {
                activity.SetTag("user.id", userContext.UserId);
            }

            if (!string.IsNullOrWhiteSpace(userContext.TenantId))
            {
                activity.SetTag("user.tenant_id", userContext.TenantId);
            }

            activity.SetTag("user.auth_type", userContext.AuthenticationType);
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["UserId"] = userContext.UserId,
            ["UserTenantId"] = userContext.TenantId,
            ["UserEmail"] = userContext.Email ?? userContext.DisplayName,
            ["UserAuthType"] = userContext.AuthenticationType
        });

        await next();
    }

    private static void PopulateUserFromEasyAuth(HttpContext httpContext, UserContext? userContext)
    {
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            var parsed = EasyAuthPrincipalParser.ParseFromHeaders(httpContext.Request.Headers);
            if (parsed is not null)
            {
                httpContext.User = parsed;
            }
        }

        if (userContext is not null)
        {
            EasyAuthPrincipalParser.PopulateUserContext(httpContext.User, userContext);
        }
    }

    private static bool IsApiRequest(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (string.Equals(path, AppRoutes.AdminApi.Root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith($"{AppRoutes.AdminApi.Root}/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var accept = httpContext.Request.Headers.Accept.ToString();
        return !string.IsNullOrWhiteSpace(accept) &&
               accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) &&
               !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteApiProblemAsync(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.Failure(problem),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: httpContext.RequestAborted);
    }
}
