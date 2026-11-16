using EstateDataCollection.Application.Models;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Models.Contracts;

public sealed record SchemaVerificationResult(
    IReadOnlyList<ValidationMessage> SyntaxErrors,
    IReadOnlyList<ValidationMessage> FixtureErrors
)
{
    public bool IsSuccessful => SyntaxErrors.Count == 0 && FixtureErrors.Count == 0;

    public IReadOnlyList<string> Errors
    {
        get => [.. SyntaxErrors.Select(e => e.Message), .. FixtureErrors.Select(e => e.Message)];
    }

    public IReadOnlyList<ValidationMessage> ValidationMessages => [.. SyntaxErrors, .. FixtureErrors];

    public IReadOnlyList<ValidationError> ToValidationErrors(string defaultPropertyPath = "schema")
    {
        return
        [
            .. SyntaxErrors.Select(e => new ValidationError(
                string.IsNullOrWhiteSpace(e.Identifier) ? defaultPropertyPath : e.Identifier,
                e.Message)),
            .. FixtureErrors.Select(e => new ValidationError(
                string.IsNullOrWhiteSpace(e.Identifier) ? "fixtures" : e.Identifier,
                e.Message))
        ];
    }
}
