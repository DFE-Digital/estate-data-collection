using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Schema.DeleteSchemaVersion;

public sealed class DeleteSchemaVersionUseCase(
    ISchemaRegistry schemaRegistry
) : IUseCase<DeleteSchemaVersionResult, DeleteSchemaVersionRequest>
{
    public async Task<Result<DeleteSchemaVersionResult>> ExecuteAsync(
        DeleteSchemaVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SchemaName) || request.Version <= 0)
        {
            return Error.Validation(
                ErrorCodes.SchemaInvalidSchedule,
                "SchemaName and positive Version are required.");
        }

        var deleteResult = await schemaRegistry.DeleteSchemaVersionAsync(
            request.SchemaName.Trim(),
            request.Version,
            cancellationToken);

        if (deleteResult.IsFailure)
        {
            return deleteResult.Error;
        }

        var meta = deleteResult.Value;
        return new DeleteSchemaVersionResult(
            SchemaName: meta.Name,
            Version: meta.Version,
            State: meta.State.ToNode(),
            DeletedAtUtc: meta.DeletedAtUtc ?? DateTimeOffset.UtcNow,
            ErrorReportPreserved: meta.HasErrorReport);
    }
}
