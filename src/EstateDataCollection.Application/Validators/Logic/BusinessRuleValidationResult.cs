using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators.Logic;

public sealed record BusinessRuleValidationResult(IReadOnlyList<ValidationError>? Errors = null)
{
    public bool IsValid => Errors is null || Errors.Count == 0;
}
