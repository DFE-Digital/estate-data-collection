using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.UseCases.Submissions.Audit;

/// <summary>
/// Shared helper that runs payload validation through <see cref="IPayloadValidationService"/>
/// when registered, or falls back to <see cref="ISchemaValidator"/> while recording canonical
/// process execution telemetry onto <see cref="SubmissionExecutionScope"/>.
/// </summary>
internal static class SubmissionValidationRunner
{
    public static async Task<Result<PayloadValidationReport>> ValidateAsync(
        SubmissionExecutionScope scope,
        IPayloadValidationService? validationService,
        ISchemaValidator fallbackValidator,
        string rawPayloadJson,
        ResolvedSchema schema,
        bool isDraftUpdate,
        CancellationToken cancellationToken)
    {
        if (validationService is IScopedPayloadValidationService scopedService)
        {
            return await scopedService.ValidatePayloadWithTraceAsync(
                rawPayloadJson,
                schema,
                isDraftUpdate,
                scope,
                cancellationToken);
        }

        if (validationService is not null)
        {
            var result = await validationService.ValidatePayloadWithTraceAsync(
                rawPayloadJson,
                schema,
                isDraftUpdate,
                cancellationToken);

            if (result.IsSuccess)
            {
                scope.ImportProcesses(result.Value.ProcessesExecuted);
            }

            return result;
        }

        var validation = await scope.ExecuteAsync(
            SubmissionProcessCatalog.JsonSchemaValidator,
            () => fallbackValidator.ValidateAsync(rawPayloadJson, schema.Schema, cancellationToken),
            val =>
            {
                var count = val.Errors?.Count ?? 0;
                return new ProcessStepDetails(
                    Outcome: count == 0
                        ? SubmissionProcessOutcome.Succeeded
                        : SubmissionProcessOutcome.ValidationErrorsFound,
                    ItemCount: count,
                    Summary: count == 0 ? "Schema structure valid" : $"{count} schema error(s)");
            });

        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new PayloadValidationReport(validation.Value.Errors ?? [], scope.Processes.ToArray());
    }
}
