using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Application.Abstraction;

public sealed record PayloadValidationReport(
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<SubmissionProcessExecution> ProcessesExecuted,
    PayloadIdentifierExtractor.ExtractionEvaluation? IdentifierEvaluation = null,
    IReadOnlyList<ValidationEngineTrace>? EngineTraces = null,
    IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? Issues = null,
    IReadOnlyList<ValidationError>? AllIssues = null)
{
    public IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>> EffectiveIssues
    {
        get { return Issues ?? ValidationIssuesHelper.GroupBySeverity(AllIssues ?? Errors); }
    }

    public bool IsSuccessful(RuleSeverity failOnMinimumSeverity = RuleSeverity.Error)
    {
        return !ValidationIssuesHelper.HasIssuesAtOrAbove(EffectiveIssues, failOnMinimumSeverity);
    }
}
