using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;

public sealed record SchemaTestSuiteItemResult(
    string SchemaName,
    int Version,
    string State,
    bool Passed,
    bool HasFixtures,
    int ValidFixtureCount,
    int InvalidFixtureCount,
    int BusinessRuleCount,
    IReadOnlyList<string> Errors,
    string? IdentifierType = null,
    string? IdentifierPointer = null,
    IReadOnlyList<InvalidFixtureOutcome>? InvalidFixtureOutcomes = null,
    IReadOnlyList<string>? Warnings = null);