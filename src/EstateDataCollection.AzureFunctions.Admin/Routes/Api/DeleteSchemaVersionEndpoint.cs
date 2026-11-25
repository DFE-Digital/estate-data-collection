using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.DeleteSchemaVersion;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Api;

/// <summary>
/// Deletes a schema version's contract/rules/fixture blobs while keeping .meta.json (State = Deleted)
/// and .error.json as permanent tombstones so the version key cannot be reused (must push version + 1).
/// Route: DELETE /api/schemas/{schemaName}/versions/{version:int}
/// </summary>
public sealed class DeleteSchemaVersionEndpoint(
    IUseCase<DeleteSchemaVersionResult, DeleteSchemaVersionRequest> deleteUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "DeleteSchemaVersion";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Delete),
            Route = AppRoutes.AdminApi.Templates.DeleteSchemaVersion)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            deleteUseCase,
            () => Task.FromResult(Result<DeleteSchemaVersionRequest>.Success(new DeleteSchemaVersionRequest(schemaName, version))),
            StatusCodes.Status200OK,
            context: $"{schemaName} v{version}",
            cancellationToken: cancellationToken);
    }
}
