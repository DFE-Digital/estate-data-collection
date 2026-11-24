using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Abstraction;

public sealed record PayloadValidationReport(
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<SubmissionProcessExecution> ProcessesExecuted,
    PayloadIdentifierExtractor.ExtractionEvaluation? IdentifierEvaluation = null,
    IReadOnlyList<ValidationEngineTrace>? EngineTraces = null);
