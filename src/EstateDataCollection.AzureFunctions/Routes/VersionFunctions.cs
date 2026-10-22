using EstateDataCollection.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Routes;

public sealed class VersionFunctions
{
    [Function(nameof(VersionAbout))]
    public IActionResult VersionAbout(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "about")] HttpRequest req)
    {
        var environment = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "UNDECLARED";

        return new OkObjectResult(new
        {
            service = nameof(EstateDataCollection),
            version = AppVersionProvider.Version,
            informativeVersion = AppVersionProvider.InformativeVersion,
            sha = AppVersionProvider.Sha,
            environment = environment,
            serverUtc = DateTimeOffset.UtcNow
        });
    }
}
