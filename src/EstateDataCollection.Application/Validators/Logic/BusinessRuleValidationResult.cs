using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators.Logic;

public sealed record BusinessRuleValidationResult(
    IReadOnlyList<ValidationError>? Errors = null,
    IReadOnlyList<ValidationEngineTrace>? EngineTraces = null)
{
    public bool IsValid
    {
        get { return Errors is null || Errors.Count == 0; }
    }
}
