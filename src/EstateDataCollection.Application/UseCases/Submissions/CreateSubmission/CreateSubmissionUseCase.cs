using System.Diagnostics;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;

public class CreateSubmissionUseCase(
    ISchemaRegistry registry,
    ISubmissionInfrastructure infrastructure,
    CreateSubmissionUseCaseInstrumentation? instrumentation = null,
    ISubmissionAuditStore? auditStore = null
) : IUseCase<CreateSubmissionResult, CreateSubmissionRequest>
{
    private readonly CreateSubmissionUseCaseInstrumentation _instrumentation =
        instrumentation ?? new CreateSubmissionUseCaseInstrumentation(NullLogger<CreateSubmissionUseCase>.Instance);

    public async Task<Result<CreateSubmissionResult>> ExecuteAsync(CreateSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        _instrumentation.CreatingSubmissionSession(request.SchemaName, request.Version);

        var processes = new List<SubmissionProcessExecution>(2);
        var schemaSw = Stopwatch.StartNew();
        ResolvedSchema resolvedSchema;

        if (request.Version.HasValue)
        {
            var @explicit =
                await registry.GetExplicitSchemaAsync(request.SchemaName, request.Version.Value, cancellationToken);
            schemaSw.Stop();

            if (@explicit.IsFailure)
            {
                _instrumentation.SchemaResolutionFailed(request.SchemaName, @explicit.Error.Description ?? string.Empty);
                return @explicit.Error;
            }

            if (@explicit.Value.Metadata.IsRetired)
            {
                _instrumentation.SchemaRetired(request.SchemaName, request.Version.Value);
                return Error.Validation(ErrorCodes.SchemaRetired);
            }

            var meta = @explicit.Value.Metadata;
            if (!meta.CanAcceptTrafficAt(DateTimeOffset.UtcNow))
            {
                _instrumentation.SchemaResolutionFailed(request.SchemaName, $"Schema v{request.Version.Value} is in state '{meta.State}' and cannot accept traffic.");
                return Error.Validation(ErrorCodes.SchemaNotActive, new { request.SchemaName, Version = request.Version.Value });
            }

            resolvedSchema = @explicit.Value;
        }
        else
        {
            var active =
                await registry.ResolveActiveSchemaAsync(request.SchemaName, DateTimeOffset.UtcNow, cancellationToken);
            schemaSw.Stop();

            if (active.IsFailure)
            {
                _instrumentation.SchemaResolutionFailed(request.SchemaName, active.Error.Description ?? string.Empty);
                return active.Error;
            }

            resolvedSchema = active.Value;
        }

        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "SchemaResolution",
            Engine = "schema-registry",
            Outcome = "Succeeded",
            DurationMs = schemaSw.ElapsedMilliseconds,
            Summary = $"{resolvedSchema.Metadata.Name} v{resolvedSchema.Metadata.Version}"
        });

        var submission = SubmissionSession.Create(
            resolvedSchema.Metadata.Name,
            resolvedSchema.Metadata.Version);

        var sessionSw = Stopwatch.StartNew();
        var outcome = await infrastructure.CreateSessionAsync(submission, cancellationToken);
        sessionSw.Stop();

        if (outcome.IsFailure)
        {
            processes.Add(new SubmissionProcessExecution
            {
                ProcessName = "BlobStorage:CreateSession",
                Engine = "azure-blob",
                Outcome = "Failed",
                DurationMs = sessionSw.ElapsedMilliseconds,
                Summary = outcome.Error.Description ?? outcome.Error.Code
            });

            await SubmissionAuditRecorder.SafeAppendAsync(
                auditStore,
                submission.SubmissionId,
                "CreateSubmission",
                submission.SchemaName,
                submission.SchemaVersion,
                "Failed",
                SubmissionAuditRecorder.MapHttpStatus(outcome.Error.Type),
                processes,
                errorCode: outcome.Error.Code,
                message: outcome.Error.Description,
                cancellationToken: cancellationToken);

            _instrumentation.SubmissionSessionCreationFailed(request.SchemaName, outcome.Error.Description ?? string.Empty);
            return outcome.Error;
        }

        processes.Add(new SubmissionProcessExecution
        {
            ProcessName = "BlobStorage:CreateSession",
            Engine = "azure-blob",
            Outcome = "Succeeded",
            DurationMs = sessionSw.ElapsedMilliseconds,
            Summary = $"Created session {submission.SubmissionId}"
        });

        await SubmissionAuditRecorder.SafeAppendAsync(
            auditStore,
            submission.SubmissionId,
            "CreateSubmission",
            submission.SchemaName,
            submission.SchemaVersion,
            "Succeeded",
            201,
            processes,
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
