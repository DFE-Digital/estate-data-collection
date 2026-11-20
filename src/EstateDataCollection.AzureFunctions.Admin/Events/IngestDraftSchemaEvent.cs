using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Events;
using Microsoft.Azure.Functions.Worker;

namespace EstateDataCollection.AzureFunctions.Admin.Events;

/// <summary>
/// Blob-triggered event handler that automatically ingests contract files (.odcs.yaml, .schema.json, .rules.json)
/// as soon as they are uploaded into the "draft-schemas" blob container.
/// Validates structural schema + quality rules (JsonLogic / CEL) + optional companion fixtures,
/// promotes valid contracts to the "schemas" container as Validated (without effective dates), and removes the processed draft file.
/// </summary>
public sealed class IngestDraftSchemaEvent(
    IUseCase<IngestDraftSchemaResult, IngestDraftSchemaRequest> ingestDraftUseCase,
    FunctionEventInstrumentation? instrumentation = null
) : FunctionEventBase(instrumentation)
{
    public const string EventName = "IngestDraftSchemaOnBlobCreated";

    [Function(EventName)]
    public Task<Result<IngestDraftSchemaResult>> HandleAsync(
        [BlobTrigger("draft-schemas/{blobPath}", Connection = "AzureWebJobsStorage")] Stream blobStream,
        string blobPath,
        CancellationToken cancellationToken)
    {
        _ = blobStream;

        return ExecuteEventAsync(
            EventName,
            blobPath ?? "<empty>",
            ingestDraftUseCase,
            () => Task.FromResult(Bind(blobPath)),
            cancellationToken);
    }

    private static Result<IngestDraftSchemaRequest> Bind(string? blobPath)
    {
        return string.IsNullOrWhiteSpace(blobPath)
            ? Error.Failure(ErrorCodes.PayloadEmpty, "Draft blob path cannot be empty.")
            : new IngestDraftSchemaRequest(BlobPath: blobPath);
    }
}
