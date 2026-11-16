using System.Text.Json.Nodes;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class SurveyPayloadBuilder
{
    private readonly JsonObject _root = new()
    {
        ["urn"] = 104521,
        ["school_name"] = "St Mary's High School",
        ["school_address"] = "1 Sanctuary Buildings, London"
    };

    public SurveyPayloadBuilder WithUrn(int urn)
    {
        _root["urn"] = urn;
        return this;
    }

    public SurveyPayloadBuilder WithoutUrn()
    {
        _root.Remove("urn");
        return this;
    }

    public SurveyPayloadBuilder WithSchoolName(string name)
    {
        _root["school_name"] = name;
        return this;
    }

    public SurveyPayloadBuilder WithoutSchoolName()
    {
        _root.Remove("school_name");
        return this;
    }

    public SurveyPayloadBuilder WithSchoolAddress(string address)
    {
        _root["school_address"] = address;
        return this;
    }

    public SurveyPayloadBuilder WithSchoolCapacity(int capacity)
    {
        _root["school_capacity"] = capacity;
        return this;
    }

    public SurveyPayloadBuilder WithProperty(string name, JsonNode? value)
    {
        _root[name] = value;
        return this;
    }

    public string BuildJson() => _root.ToJsonString();
}
