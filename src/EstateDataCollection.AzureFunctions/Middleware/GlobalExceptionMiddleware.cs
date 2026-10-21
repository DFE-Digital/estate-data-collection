using EstateDataCollection.AzureFunctions.Extensions;
using EstateDataCollection.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.AzureFunctions.Middleware;

public class GlobalExceptionMiddleware(ILogger<GlobalExceptionMiddleware> logger)
    : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var httpContext = context.GetHttpContext();
            if (httpContext is null)
            {
                throw; // non-HTTP trigger: let the host handle retry/dead-letter
            }

            logger.LogError(ex, "Unhandled exception in {Function}", context.FunctionDefinition.Name);

            var problem = ErrorCodes.None.ToProblem(StatusCodes.Status500InternalServerError);
            problem.Instance = httpContext.Request.Path;
            problem.Extensions["traceId"] = context.InvocationId;

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: httpContext.RequestAborted);
        }
    }
}