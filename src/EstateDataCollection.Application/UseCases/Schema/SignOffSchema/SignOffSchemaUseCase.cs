using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Messages;
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
                SchemaValidationMessages.SchemaNameAndPositiveVersionRequired);
        }

        if (string.IsNullOrWhiteSpace(request.SignedOffBy))
        {
            return Error.Validation(
                ErrorCodes.SchemaSignOffInvalidState,
                SchemaValidationMessages.SignedOffByRequired,
                [new ValidationError("signedOffBy", SchemaValidationMessages.SignedOffByIdentityRequired, Severity: RuleSeverity.Error)]);
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
                DomainMessages.OnlyUnvalidatedCanBeSignedOff(metadata.State),
                [new ValidationError("state", SchemaValidationMessages.SchemaVersionInState(metadata.Name, metadata.Version, metadata.State), Severity: RuleSeverity.Error)]);
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
