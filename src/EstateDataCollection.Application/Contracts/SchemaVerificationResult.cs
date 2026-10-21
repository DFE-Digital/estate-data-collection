namespace EstateDataCollection.Application.Contracts;

public sealed record SchemaVerificationResult(
    IReadOnlyList<string> SyntaxErrors,
    IReadOnlyList<string> FixtureErrors
)
{
    public bool IsSuccessful => Errors.Count == 0;
    public IReadOnlyList<string> Errors => [.. SyntaxErrors, .. FixtureErrors];
}