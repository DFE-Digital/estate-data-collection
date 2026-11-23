using EstateDataCollection.Application.Models.Contracts;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SchemaCatalogueEntryBuilder
{
    private string _schemaName = "edc_payload";
    private int? _activeVersion;
    private readonly List<SchemaVersionSummary> _versions = [];
    private List<SchemaFieldDescriptor>? _activeVersionFields;

    public SchemaCatalogueEntryBuilder WithName(string schemaName)
    {
        _schemaName = schemaName;
        return this;
    }

    public SchemaCatalogueEntryBuilder WithActiveVersion(int? activeVersion)
    {
        _activeVersion = activeVersion;
        return this;
    }

    public SchemaCatalogueEntryBuilder WithVersion(SchemaVersionSummary version)
    {
        _versions.Add(version);
        return this;
    }

    public SchemaCatalogueEntryBuilder WithVersion(Action<SchemaVersionSummaryBuilder> configure)
    {
        var builder = new SchemaVersionSummaryBuilder().WithSchema(_schemaName);
        configure(builder);
        _versions.Add(builder.Build());
        return this;
    }

    public SchemaCatalogueEntryBuilder WithActiveVersionField(SchemaFieldDescriptor field)
    {
        _activeVersionFields ??= [];
        _activeVersionFields.Add(field);
        return this;
    }

    public SchemaCatalogueEntry Build()
    {
        return new SchemaCatalogueEntry(
            SchemaName: _schemaName,
            ActiveVersion: _activeVersion,
            Versions: _versions.ToArray(),
            ActiveVersionFields: _activeVersionFields?.ToArray());
    }
}
