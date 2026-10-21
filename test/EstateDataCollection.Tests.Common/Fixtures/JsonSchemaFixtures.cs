namespace EstateDataCollection.Tests.Common.Fixtures;

public static class JsonSchemaFixtures
{
    public const string ValidV1Schema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "title": "edc_payload",
      "schemaVersion": 1,
      "type": "object",
      "properties": {
        "urn": { "type": "integer" },
        "school_name": { "type": "string" },
        "school_address": { "type": "string" },
        "school_capacity": { "type": ["integer", "null"] }
      },
      "required": ["urn", "school_name", "school_address"],
      "additionalProperties": false
    }
    """;

    public const string ValidV2SchemaWithoutCapacity = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "title": "edc_payload",
      "schemaVersion": 2,
      "type": "object",
      "properties": {
        "urn": { "type": "integer" },
        "school_name": { "type": "string" },
        "school_address": { "type": "string" },
        "local_authority": { "type": ["string", "null"] }
      },
      "required": ["urn", "school_name", "school_address"],
      "additionalProperties": false
    }
    """;
}
