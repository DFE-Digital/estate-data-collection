using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Tests.Common.Fuzzing;

public sealed record FuzzSchemaContract(
    FuzzSchemaProfile Profile,
    string SchemaNamePrefix,
    string SchemaJson,
    string? RulesJson,
    SchemaIdentifierConfig IdentifierConfig);