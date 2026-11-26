using System.Diagnostics;
using EstateDataCollection.AzureFunctions.Public.Options;
using EstateDataCollection.AzureFunctions.Public.Security;
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

namespace EstateDataCollection.AzureFunctions.Public.Middleware;

/// <summary>
/// Resolves <see cref="IActorContext"/> on every Public API HTTP invocation from DfE Find and Use an API
/// (FaUAPI) subscription keys / forwarded APIM metadata headers (and future OAuth 2.0 Bearer tokens),
/// enriches logs and OpenTelemetry traces, and enforces actor presence on endpoints not marked
/// <c>[AllowAnonymous]</c> when <see cref="PublicAuthOptions.Enabled"/> is true.
/// </summary>
public sealed class ActorContextMiddleware(
    IOptions<PublicAuthOptions> options,
    ILogger<ActorContextMiddleware> logger,
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

        var actorContext = context.InstanceServices.GetService<ActorContext>();
        var metadata = metadataResolver?.Resolve(context);
        await InvokeHttpAsync(httpContext, () => next(context), actorContext, metadata);
    }

    public async Task InvokeHttpAsync(
        HttpContext httpContext,
        Func<Task> next,
        ActorContext? actorContext = null,
        EndpointAuthMetadata? metadata = null)
    {
        var authOptions = options.Value;
        var resolvedTarget = actorContext ?? httpContext.RequestServices?.GetService<ActorContext>();
        var actor = FaUApiActorResolver.Resolve(httpContext, authOptions, resolvedTarget);

        var isAnonymousEndpoint = (metadata ?? EndpointAuthMetadata.DefaultProtected).AllowAnonymous;

        if (actor.IsAuthenticated)
        {
            var activity = Activity.Current;
            if (activity is not null)
            {
                activity.SetTag("actor.id", actor.ActorId);
                activity.SetTag("actor.auth_type", actor.AuthenticationType);
                if (!string.IsNullOrWhiteSpace(actor.SubscriptionId))
                {
                    activity.SetTag("actor.subscription_id", actor.SubscriptionId);
                }
            }
        }
        else if (!isAnonymousEndpoint && authOptions.Enabled && authOptions.RequireActorOnAuthenticatedRoutes)
        {
            logger.LogWarning(
                "Rejected unauthenticated Public API request to {Method} {Path}; missing FaUAPI actor credentials.",
                httpContext.Request.Method,
                httpContext.Request.Path);

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Auth.Unauthorized",
                Detail = $"A valid DfE FaUAPI subscription key ({authOptions.SubscriptionKeyHeader}) or OAuth 2.0 Bearer token is required.",
                Instance = httpContext.Request.Path
            };

            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response.WriteAsJsonAsync(
                ApiResponse<object>.Failure(problem),
                options: null,
                contentType: "application/problem+json",
                cancellationToken: httpContext.RequestAborted);
            return;
        }

        using var scope = actor.IsAuthenticated
            ? logger.BeginScope(new Dictionary<string, object?>
            {
                ["ActorId"] = actor.ActorId,
                ["ActorName"] = actor.ActorName,
                ["ActorAuthType"] = actor.AuthenticationType,
                ["FaUApiSubscriptionId"] = actor.SubscriptionId
            })
            : null;

        await next();
    }
}
