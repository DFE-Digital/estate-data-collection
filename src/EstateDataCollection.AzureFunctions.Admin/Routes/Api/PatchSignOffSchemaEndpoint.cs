using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.SignOffSchema;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Routes.Api;

/// <summary>
/// Explicitly signs off an Unvalidated schema version (warnings-only) so it transitions to Validated and can be scheduled.
/// Route: PATCH /api/schemas/{schemaName}/versions/{version:int}/signoff
/// </summary>
public sealed class PatchSignOffSchemaEndpoint(
    IUseCase<SignOffSchemaResult, SignOffSchemaRequest> signOffUseCase,
    FunctionEndpointInstrumentation? instrumentation = null
) : FunctionEndpointBase(instrumentation)
{
    public const string EndpointName = "SignOffSchema";

    [Function(EndpointName)]
    public Task<IActionResult> HandleAsync(
        [HttpTrigger(AuthorizationLevel.Function, nameof(HttpMethods.Patch),
            Route = AppRoutes.AdminApi.Templates.SignOffSchema)]
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            EndpointName,
            signOffUseCase,
            () => BindAsync(http, schemaName, version, cancellationToken),
            StatusCodes.Status200OK,
            context: $"{schemaName} v{version}",
            cancellationToken: cancellationToken);
    }

    private static async Task<Result<SignOffSchemaRequest>> BindAsync(
        HttpRequest http,
        string schemaName,
        int version,
        CancellationToken cancellationToken)
    {
        var rawBody = await http.ReadRequestBodyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return new SignOffSchemaRequest(schemaName, version, string.Empty);
        }

        var dtoResult = DeserialiseJson<SignOffSchemaBodyDto>(rawBody, ErrorMessages.InvalidSignOffRequestBody);
        if (dtoResult.IsFailure)
        {
            return dtoResult.Error;
        }

        return new SignOffSchemaRequest(schemaName, version, dtoResult.Value.SignedOffBy ?? string.Empty);
    }
}
