using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Abstraction;

public sealed record PayloadEvaluationResult(
    ResolvedSchema Schema,
    IReadOnlyList<ValidationError> ValidationErrors,
    PayloadIdentifierExtractor.ExtractionEvaluation IdentifierEvaluation,
    SubmissionAuditIdentifier? AuditIdentifier);