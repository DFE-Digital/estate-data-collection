namespace EstateDataCollection.Domain.Schema;

public sealed record InvalidFixtureSpec(
    string PayloadJson,
    IReadOnlyList<string> ExpectedErrors,
    string? Description = null,
    bool HasWrapper = false);