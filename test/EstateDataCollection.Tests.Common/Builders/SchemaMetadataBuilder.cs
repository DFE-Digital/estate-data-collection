using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Fixtures;

namespace EstateDataCollection.Tests.Common.Builders;

public class SchemaMetadataBuilder
{
    private string _name = "edc_payload";
    private int _version = 1;
    private string _jsonSchema = JsonSchemaFixtures.ValidV1Schema;
    private DateTimeOffset? _effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
    private DateTimeOffset? _effectiveTo;

    public SchemaMetadataBuilder WithName(string name) { _name = name; return this; }
    public SchemaMetadataBuilder WithVersion(int version) { _version = version; return this; }
    public SchemaMetadataBuilder WithSchema(string json) { _jsonSchema = json; return this; }
    public SchemaMetadataBuilder ScheduledInFuture(int daysFromNow = 7)
    {
        _effectiveFrom = DateTimeOffset.UtcNow.AddDays(daysFromNow);
        return this;
    }

    public SchemaMetadata Build()
    {
        var meta = SchemaMetadata.CreateDraft(_name, _version, _jsonSchema);
        if (_effectiveFrom.HasValue)
        {
            meta.ScheduleActivation(_effectiveFrom.Value, _effectiveTo);
        }
        return meta;
    }
}
