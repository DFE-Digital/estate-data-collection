using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema.VerifySchema;

public sealed record VerifySchemaResult(
    string Message,
    SchemaIdentifierConfig? ResolvedIdentifierConfig = null,
    IReadOnlyList<InvalidFixtureOutcome>? InvalidFixtureOutcomes = null,
    IReadOnlyList<string>? Warnings = null);
