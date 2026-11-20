using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Fixtures;
using Json.Schema;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class ResolvedSchemaBuilder
{
    private SchemaMetadata? _metadata;
    private readonly SchemaMetadataBuilder _metadataBuilder = new();
    private string _schemaJson = JsonSchemaFixtures.ValidV1Schema;
    private IReadOnlyList<BusinessRule> _rules = [];

    public ResolvedSchemaBuilder WithMetadata(SchemaMetadata metadata)
    {
        _metadata = metadata;
        _schemaJson = metadata.RawJsonSchema;
        return this;
    }

    public ResolvedSchemaBuilder ConfigureMetadata(Action<SchemaMetadataBuilder> configure)
    {
        configure(_metadataBuilder);
        return this;
    }

    public ResolvedSchemaBuilder WithSchemaJson(string schemaJson)
    {
        _schemaJson = schemaJson;
        _metadataBuilder.WithSchema(schemaJson);
        return this;
    }

    public ResolvedSchemaBuilder WithRules(IReadOnlyList<BusinessRule> rules)
    {
        _rules = rules;
        return this;
    }

    public ResolvedSchema Build()
    {
        var metadata = _metadata ?? _metadataBuilder.Build();
        var compiled = JsonSchema.FromText(_schemaJson);
        return new ResolvedSchema(metadata, compiled, _rules);
    }
}
