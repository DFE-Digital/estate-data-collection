using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema.SignOffSchema;

public sealed class SignOffSchemaUseCase(
    ISchemaRegistry schemaRegistry
) : IUseCase<SignOffSchemaResult, SignOffSchemaRequest>
{
    public async Task<Result<SignOffSchemaResult>> ExecuteAsync(
        SignOffSchemaRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SchemaName) || request.Version <= 0)
        {
            return Error.Validation(
                ErrorCodes.SchemaSignOffInvalidState,
                "SchemaName and positive Version are required.");
        }

        if (string.IsNullOrWhiteSpace(request.SignedOffBy))
        {
            return Error.Validation(
                ErrorCodes.SchemaSignOffInvalidState,
                "signedOffBy is required to sign off an Unvalidated schema.",
                [new ValidationError("signedOffBy", "Signed-off-by identity is required.", Severity: RuleSeverity.Error)]);
        }

        var existingResult = await schemaRegistry.GetExplicitSchemaAsync(
            request.SchemaName.Trim(),
            request.Version,
            cancellationToken);

        if (existingResult.IsFailure)
        {
            return existingResult.Error;
        }

        var metadata = existingResult.Value.Metadata;
        if (metadata.State != SchemaLifecycleState.Unvalidated)
        {
            return Error.Validation(
                ErrorCodes.SchemaSignOffInvalidState,
                $"Only Unvalidated schemas can be signed off. Current state: '{metadata.State}'.",
                [new ValidationError("state", $"Schema '{metadata.Name}' v{metadata.Version} is in '{metadata.State}' state.", Severity: RuleSeverity.Error)]);
        }

        metadata.SignOff(request.SignedOffBy);

        var updateResult = await schemaRegistry.UpdateSchemaMetadataAsync(metadata, cancellationToken);
        if (updateResult.IsFailure)
        {
            return updateResult.Error;
        }

        return new SignOffSchemaResult(
            SchemaName: metadata.Name,
            Version: metadata.Version,
            State: metadata.State.ToNode(),
            SignedOffBy: metadata.SignedOffBy!,
            SignedOffAtUtc: metadata.SignedOffAtUtc!.Value);
    }
}
