namespace EstateDataCollection.Tests.Common.Fixtures;

public static class SurveyPayloadFixtures
{
    public const string ValidPayloadJson = """
    {
      "urn": 104521,
      "school_name": "St Mary's High School",
      "school_address": "High Street, Leeds",
      "school_capacity": 450
    }
    """;

    public const string MissingUrnJson = """
    {
      "school_name": "St Mary's High School",
      "school_address": "High Street, Leeds"
    }
    """;

    public const string AdditionalPropertyJson = """
    {
      "urn": 104521,
      "school_name": "St Mary's High School",
      "school_address": "High Street, Leeds",
      "unexpected_extra_field": "disallowed"
    }
    """;
}
