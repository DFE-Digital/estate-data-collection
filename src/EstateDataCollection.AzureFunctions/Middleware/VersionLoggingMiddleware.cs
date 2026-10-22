using EstateDataCollection.Common;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace EstateDataCollection.AzureFunctions.Middleware;

/// <summary>
/// Enriches every invocation log context with the build-stamped CalVer and runtime environment.
/// </summary>
public sealed class VersionLoggingMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly string EnvironmentName = 
        Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "UNDECLARED";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var logger = context.GetLogger<VersionLoggingMiddleware>();
        
        using (logger.BeginScope(new Dictionary<string, object>
        {
            [nameof(AppVersionProvider.Keys.Version)] = AppVersionProvider.Version,
            [nameof(AppVersionProvider.Keys.Sha)] = AppVersionProvider.Sha,
            ["Environment"] = EnvironmentName,
        }))
        {
            await next(context);
        }
    }
}
