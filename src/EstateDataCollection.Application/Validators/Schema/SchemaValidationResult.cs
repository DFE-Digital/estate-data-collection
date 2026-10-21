using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Validators.Schema;

public sealed record SchemaValidationResult(IReadOnlyList<ValidationError>? Errors = null)
{
    public bool IsValid => Errors is null || Errors.Count == 0;
}