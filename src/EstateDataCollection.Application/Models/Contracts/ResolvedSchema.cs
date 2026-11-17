using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Domain.Schema;
using Json.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

public sealed record ResolvedSchema(
    SchemaMetadata Metadata,
    JsonSchema Schema,
    IReadOnlyList<BusinessRule>? Rules = null,
    string? RawRulesJson = null,
    IReadOnlyList<string>? ValidFixtures = null,
    IReadOnlyList<string>? InvalidFixtures = null
);
