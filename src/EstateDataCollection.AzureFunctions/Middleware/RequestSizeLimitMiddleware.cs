using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.AzureFunctions.Middleware;

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

        // 1. Fast fail if Content-Length header exceeds the configured limit
        if (httpContext.Request.ContentLength.HasValue && httpContext.Request.ContentLength.Value > maxBytes)
        {
            logger.LogWarning(
                "Request payload size {ContentLength} bytes exceeds configured limit of {MaxSizeMb} MB on path {Path}",
                httpContext.Request.ContentLength.Value,
                options.Value.MaxSizeMb,
                httpContext.Request.Path);

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "Payload Too Large",
                Detail = $"The request payload exceeds the configured maximum limit of {options.Value.MaxSizeMb} MB.",
                Instance = httpContext.Request.Path
            };
            problem.Extensions["errorCode"] = "Payload.TooLarge";
            problem.Extensions["maxSizeMb"] = options.Value.MaxSizeMb;

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