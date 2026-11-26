using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema.VerifySchema;

public sealed record VerifySchemaResult(
    string Message,
    SchemaIdentifierConfig? ResolvedIdentifierConfig = null,
    IReadOnlyList<InvalidFixtureOutcome>? InvalidFixtureOutcomes = null,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<ValidationEngineTrace>? EngineTraces = null,
    bool IsSuccessful = true,
    IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? Issues = null,
    IReadOnlyList<string>? SyntaxErrors = null,
    IReadOnlyList<string>? FixtureErrors = null,
    IReadOnlyList<ValidationError>? AnalyserFindings = null
);
