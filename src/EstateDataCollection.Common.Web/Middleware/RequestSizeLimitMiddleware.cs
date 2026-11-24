using EstateDataCollection.Common.Web.Extensions;
using EstateDataCollection.Common.Web.Models.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Common.Web.Middleware;

public class RequestSizeLimitMiddleware(
    IOptions<RequestSizeLimitOptions> options,
    ILogger<RequestSizeLimitMiddleware> logger
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

        var maxBytes = options.Value.MaxSizeBytes;
        httpContext.Items[HttpRequestExtensions.MaxBodySizeItemKey] = maxBytes;

        // 1. Fast fail if Content-Length header exceeds the configured limit
        if (httpContext.Request.ContentLength.HasValue && httpContext.Request.ContentLength.Value > maxBytes)
        {
            logger.LogWarning(
                "Request payload size {ContentLength} bytes exceeds configured limit of {MaxSizeMb} MB on path {Path}",
                httpContext.Request.ContentLength.Value,
                options.Value.MaxSizeMb,
                httpContext.Request.Path);

            var problem = HttpRequestExtensions.CreatePayloadTooLargeProblem(maxBytes, httpContext.Request.Path);

            httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await httpContext.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: httpContext.RequestAborted);

            return; // Short-circuit pipeline
        }

        await next(context);
    }
}
