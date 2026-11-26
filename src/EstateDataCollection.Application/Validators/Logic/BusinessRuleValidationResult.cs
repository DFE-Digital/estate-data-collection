using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Helpers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators.Logic;

public sealed record BusinessRuleValidationResult(
    IReadOnlyList<ValidationError>? Errors = null,
    IReadOnlyList<ValidationEngineTrace>? EngineTraces = null,
    IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? Issues = null)
{
    public IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>> EffectiveIssues => Issues ?? ValidationIssuesHelper.GroupBySeverity(Errors);

    public bool IsValid => Errors is null || Errors.Count == 0;

    public bool HasBlockingIssues(RuleSeverity failOnMinimumSeverity = RuleSeverity.Error)
    {
        return ValidationIssuesHelper.HasIssuesAtOrAbove(Errors, failOnMinimumSeverity);
    }
}
