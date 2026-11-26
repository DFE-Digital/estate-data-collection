using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Fixtures;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SchemaMetadataBuilder
{
    private string _name = "edc_payload";
    private int _version = 1;
    private string _jsonSchema = JsonSchemaFixtures.ValidV1Schema;
    private DateTimeOffset? _effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
    private DateTimeOffset? _effectiveTo;
    private SchemaIdentifierConfig? _identifierConfig;
    private SchemaValidationPolicy? _validationPolicy;
    private bool _markValidated;
    private bool _retire;

    public SchemaMetadataBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public SchemaMetadataBuilder WithUniqueName(string prefix = "test_schema")
    {
        _name = $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, 48)];
        return this;
    }

    public SchemaMetadataBuilder WithVersion(int version)
    {
        _version = version;
        return this;
    }

    public SchemaMetadataBuilder WithSchema(string json)
    {
        _jsonSchema = json;
        return this;
    }

    public SchemaMetadataBuilder WithEffectiveFrom(DateTimeOffset effectiveFrom)
    {
        _effectiveFrom = effectiveFrom;
        _markValidated = false;
        return this;
    }

    public SchemaMetadataBuilder WithEffectiveTo(DateTimeOffset effectiveTo)
    {
        _effectiveTo = effectiveTo;
        return this;
    }

    public SchemaMetadataBuilder Unscheduled()
    {
        _effectiveFrom = null;
        _effectiveTo = null;
        _markValidated = true;
        return this;
    }

    public SchemaMetadataBuilder Retired()
    {
        _retire = true;
        return this;
    }

    public SchemaMetadataBuilder WithIdentifierConfig(SchemaIdentifierConfig? identifierConfig)
    {
        _identifierConfig = identifierConfig;
        return this;
    }

    public SchemaMetadataBuilder WithValidationPolicy(SchemaValidationPolicy? validationPolicy)
    {
        _validationPolicy = validationPolicy;
        return this;
    }

    public SchemaMetadataBuilder WithUrnPointer(string jsonPointer, bool required = true)
    {
        _identifierConfig = new SchemaIdentifierConfig
        {
            Type = "URN",
            JsonPointer = jsonPointer,
            Required = required
        };
        return this;
    }

    public SchemaMetadataBuilder WithoutUrn()
    {
        _identifierConfig = new SchemaIdentifierConfig
        {
            Type = "None",
            JsonPointer = null,
            Required = false
        };
        return this;
    }

    public SchemaMetadataBuilder ScheduledInFuture(int daysFromNow = 7)
    {
        _effectiveFrom = DateTimeOffset.UtcNow.AddDays(daysFromNow);
        _markValidated = false;
        return this;
    }

    public SchemaMetadata Build()
    {
        var meta = SchemaMetadata.CreateDraft(_name, _version, _jsonSchema, _identifierConfig, _validationPolicy);

        if (_effectiveFrom.HasValue)
        {
            meta.ScheduleActivation(_effectiveFrom.Value, _effectiveTo);
        }
        else if (_markValidated)
        {
            meta.MarkValidated();
        }

        if (_retire)
        {
            meta.Retire();
        }

        return meta;
    }
}
