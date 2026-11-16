using System.Text;

namespace EstateDataCollection.Tests.Common.Builders;

public sealed class OdcsContractYamlBuilder
{
    private string _version = "1.0.0";
    private string _id = "edc-test-contract";
    private string _name = "TestContract";
    private bool _nestedSchoolUrn;

    public OdcsContractYamlBuilder WithVersion(string version)
    {
        _version = version;
        return this;
    }

    public OdcsContractYamlBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    public OdcsContractYamlBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public OdcsContractYamlBuilder WithNestedSchoolUrn()
    {
        _nestedSchoolUrn = true;
        return this;
    }

    public string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"version: \"{_version}\"");
        sb.AppendLine("kind: DataContract");
        sb.AppendLine("apiVersion: v3.2.0");
        sb.AppendLine($"id: {_id}");
        sb.AppendLine($"name: {_name}");
        sb.AppendLine("schema:");
        sb.AppendLine("  - name: submission");
        sb.AppendLine("    logicalType: object");
        sb.AppendLine("    properties:");

        if (_nestedSchoolUrn)
        {
            sb.AppendLine("      - name: school");
            sb.AppendLine("        logicalType: object");
            sb.AppendLine("        required: true");
            sb.AppendLine("        properties:");
            sb.AppendLine("          - name: urn");
            sb.AppendLine("            logicalType: string");
            sb.AppendLine("            required: true");
        }
        else
        {
            sb.AppendLine("      - name: urn");
            sb.AppendLine("        logicalType: integer");
            sb.AppendLine("        required: true");
        }

        return sb.ToString();
    }
}
