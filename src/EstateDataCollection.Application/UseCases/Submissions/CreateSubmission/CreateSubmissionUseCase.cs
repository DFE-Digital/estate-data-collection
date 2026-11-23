using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Extensions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;

public class CreateSubmissionUseCase(
    ISchemaRegistry registry,
    ISubmissionInfrastructure infrastructure,
    CreateSubmissionUseCaseInstrumentation? instrumentation = null,
    ISubmissionAuditStore? auditStore = null,
    ProcessExecutionInstrumentation? processInstrumentation = null
) : IUseCase<CreateSubmissionResult, CreateSubmissionRequest>
{
    private readonly CreateSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new CreateSubmissionUseCaseInstrumentation(NullLogger<CreateSubmissionUseCase>.Instance);

    public async Task<Result<CreateSubmissionResult>> ExecuteAsync(CreateSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.CreatingSubmissionSession(request.SchemaName, request.Version);

        using var scope = SubmissionExecutionScope.Start(
            "CreateSubmission",
            instrumentation: processInstrumentation,
            auditStore: auditStore,
            initialCapacity: 2);

        var schemaResult = await scope.ExecuteAsync(
            SubmissionProcessCatalog.SchemaResolution,
            async () =>
            {
                if (!request.Version.HasValue)
                {
                    return await registry.ResolveActiveSchemaAsync(request.SchemaName, DateTimeOffset.UtcNow, cancellationToken);
                }

                var @explicit = await registry.GetExplicitSchemaAsync(request.SchemaName, request.Version.Value, cancellationToken);
                if (@explicit.IsFailure)
                {
                    return @explicit.Error;
                }

                if (@explicit.Value.Metadata.IsRetired)
                {
                    _instrumentation.SchemaRetired(request.SchemaName, request.Version.Value);
                    return Result<ResolvedSchema>.Failure(Error.Validation(ErrorCodes.SchemaRetired));
                }

                var meta = @explicit.Value.Metadata;
                if (!meta.CanAcceptTrafficAt(DateTimeOffset.UtcNow))
                {
                    return Result<ResolvedSchema>.Failure(Error.Validation(
                        ErrorCodes.SchemaNotActive,
                        new { request.SchemaName, Version = request.Version.Value }));
                }

                return @explicit;
            },
            resolved => new ProcessStepDetails(Summary: $"{resolved.Metadata.Name} v{resolved.Metadata.Version}"));

        if (schemaResult.IsFailure)
        {
            if (!string.Equals(schemaResult.Error.Code, ErrorCodes.SchemaRetired.GetName(), StringComparison.Ordinal))
            {
                var detail = string.Equals(schemaResult.Error.Code, ErrorCodes.SchemaNotActive.GetName(), StringComparison.Ordinal) && request.Version.HasValue
                    ? $"Schema v{request.Version.Value} cannot accept traffic."
                    : schemaResult.Error.Description ?? string.Empty;
                _instrumentation.SchemaResolutionFailed(request.SchemaName, detail);
            }

            return schemaResult.Error;
        }

        var resolvedSchema = schemaResult.Value;
        var submission = SubmissionSession.Create(
            resolvedSchema.Metadata.Name,
            resolvedSchema.Metadata.Version);
        scope.SetSubmissionId(submission.SubmissionId);

        var outcome = await scope.ExecuteAsync(
            SubmissionProcessCatalog.CreateSession,
            () => infrastructure.CreateSessionAsync(submission, cancellationToken),
            _ => new ProcessStepDetails(Summary: $"Created session {submission.SubmissionId}"));

        if (outcome.IsFailure)
        {
            await scope.RecordErrorAuditAsync(
                submission.SchemaName,
                submission.SchemaVersion,
                outcome.Error,
                outcomeStatus: "Failed",
                cancellationToken: cancellationToken);

            _instrumentation.SubmissionSessionCreationFailed(request.SchemaName, outcome.Error.Description ?? string.Empty);
            return outcome.Error;
        }

        await scope.RecordAuditAsync(
            submission.SchemaName,
            submission.SchemaVersion,
            "Succeeded",
            201,
            message: $"Draft session created for {submission.SchemaName} v{submission.SchemaVersion}",
            cancellationToken: cancellationToken);

        _instrumentation.SubmissionSessionCreated(
            submission.SubmissionId,
            submission.SchemaName,
            submission.SchemaVersion);

        return new CreateSubmissionResult(
            submission.SubmissionId,
            submission.SchemaName,
            submission.SchemaVersion,
            submission.CreatedAtUtc);
    }
}
