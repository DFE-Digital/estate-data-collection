using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.UseCases.Schema.SubmitSchema;

public sealed record SubmitSchemaResult(
    string SchemaName,
    int Version,
    Node<int> State,
    string Sha256Hash,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    SchemaIdentifierConfig? IdentifierConfig = null,
    SchemaValidationPolicy? ValidationPolicy = null,
    bool HasErrorReport = false,
    string? ErrorSummary = null,
    IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>>? Issues = null
);
