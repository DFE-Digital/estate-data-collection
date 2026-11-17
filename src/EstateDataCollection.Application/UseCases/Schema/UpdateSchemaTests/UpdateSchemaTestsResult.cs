using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;

public sealed record UpdateSchemaTestsResult(
    string SchemaName,
    int Version,
    int ValidFixtureCount,
    int InvalidFixtureCount,
    int BusinessRuleCount,
    DateTimeOffset ExecutedAtUtc,
    string AppVersion,
    IReadOnlyList<InvalidFixtureOutcome>? InvalidFixtureOutcomes = null,
    IReadOnlyList<string>? Warnings = null);
