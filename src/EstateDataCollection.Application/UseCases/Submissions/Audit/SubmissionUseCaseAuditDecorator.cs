using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.UseCases.Submissions.Audit;

/// <summary>
/// Scrutor decorator over <see cref="IUseCase{TResult, TRequest}"/> that provides a unified
/// ambient trace scope, OpenTelemetry span, and audit trail persistence boundary for
/// <see cref="IAuditedSubmissionRequest"/> operations.
/// Non-submission use cases pass through without overhead.
/// </summary>
public sealed class SubmissionUseCaseAuditDecorator<TResult, TRequest>(
    IUseCase<TResult, TRequest> inner,
    ISubmissionAuditStore? auditStore = null,
    ProcessExecutionInstrumentation? processInstrumentation = null
) : IUseCase<TResult, TRequest>
{
    public async Task<Result<TResult>> ExecuteAsync(
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is not IAuditedSubmissionRequest auditedRequest)
        {
            return await inner.ExecuteAsync(request, cancellationToken);
        }

        using var scope = SubmissionExecutionScope.Start(
            auditedRequest.AuditActionName,
            auditedRequest.InitialSubmissionId,
            processInstrumentation,
            auditStore);

        var result = await inner.ExecuteAsync(request, cancellationToken);

        if (!scope.IsAuditEnabled || scope.SubmissionId == Guid.Empty)
        {
            if (result.IsFailure)
            {
                scope.MarkRootFailed(result.Error);
            }

            return result;
        }

        if (result.IsSuccess)
        {
            await scope.RecordAuditAsync(
                scope.SchemaName,
                scope.SchemaVersion,
                scope.OutcomeStatus ?? "Succeeded",
                scope.HttpStatus ?? 200,
                message: scope.Message,
                validationErrors: scope.ValidationErrors,
                identifier: scope.Identifier,
                details: scope.Details,
                cancellationToken: cancellationToken);
        }
        else
        {
            var failureValidationErrors = scope.ValidationErrors
                ?? (result.Error.ValidationErrors is { Count: > 0 } ? result.Error.ValidationErrors : null);

            await scope.RecordErrorAuditAsync(
                scope.SchemaName,
                scope.SchemaVersion,
                result.Error,
                outcomeStatus: scope.FailureOutcomeStatus,
                message: scope.Message,
                validationErrors: failureValidationErrors,
                identifier: scope.Identifier,
                details: scope.Details,
                cancellationToken: cancellationToken);
        }

        return result;
    }
}
