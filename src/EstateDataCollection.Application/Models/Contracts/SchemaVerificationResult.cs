using EstateDataCollection.Application.Models;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

public sealed record SchemaVerificationResult(
    IReadOnlyList<ValidationMessage> SyntaxErrors,
    IReadOnlyList<ValidationMessage> FixtureErrors,
    SchemaIdentifierConfig? ResolvedIdentifierConfig = null,
    IReadOnlyList<InvalidFixtureOutcome>? InvalidFixtureOutcomes = null,
    IReadOnlyList<ValidationMessage>? Warnings = null
)
{
    public bool IsSuccessful
    {
        get { return SyntaxErrors.Count == 0 && FixtureErrors.Count == 0; }
    }

    public IReadOnlyList<string> Errors
    {
        get { return [.. SyntaxErrors.Select(e => e.Message), .. FixtureErrors.Select(e => e.Message)]; }
    }

    public IReadOnlyList<string> WarningMessages
    {
        get { return [.. (Warnings ?? []).Select(w => w.Message)]; }
    }

    public IReadOnlyList<ValidationMessage> ValidationMessages
    {
        get { return [.. SyntaxErrors, .. FixtureErrors]; }
    }

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
