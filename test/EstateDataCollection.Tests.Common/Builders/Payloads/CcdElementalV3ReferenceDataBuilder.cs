using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Tests.Common.Builders.Payloads;

public sealed class CcdElementalV3ReferenceDataBuilder
{
    public const string DefaultSchemaName = "ccd-elemental-v3";

    private string[] _productTypes =
    [
        "Boiler",
        "Roof",
        "Window",
        "Electrical Distribution",
        "Fire Alarm",
        "External Paving"
    ];

    private string[] _nrm3Codes =
    [
        "2.3.1",
        "2.3.2",
        "2.5.1",
        "5.1.1",
        "5.6.1",
        "5.8.1",
        "8.1.1"
    ];

    private Dictionary<string, string[]> _repairItemsByProductType = new(StringComparer.Ordinal)
    {
        ["Boiler"] = ["Boiler Valve", "Burner Assembly", "Flue Replacement", "Circulation Pump"],
        ["Roof"] = ["Roof Tile", "Lead Flashing", "Flat Roof Felt", "Gutter Replacement"],
        ["Window"] = ["Window Glazing Unit", "Window Frame", "Ironmongery"],
        ["Electrical Distribution"] = ["Distribution Board", "Sub-Main Cable", "Circuit Breaker"],
        ["Fire Alarm"] = ["Smoke Detector", "Control Panel", "Sounder Beacon"],
        ["External Paving"] = ["Tarmac Resurfacing", "Kerb Replacement", "Drainage Channel"]
    };

    public CcdElementalV3ReferenceDataBuilder WithProductTypes(params string[] productTypes)
    {
        _productTypes = productTypes;
        return this;
    }

    public CcdElementalV3ReferenceDataBuilder WithNrm3Codes(params string[] nrm3Codes)
    {
        _nrm3Codes = nrm3Codes;
        return this;
    }

    public string BuildProductTypesJson() =>
        JsonSerializer.Serialize(_productTypes, new JsonSerializerOptions { WriteIndented = true });

    public string BuildNrm3CodesJson() =>
        JsonSerializer.Serialize(_nrm3Codes, new JsonSerializerOptions { WriteIndented = true });

    public string BuildRepairItemsByProductTypeJson() =>
        JsonSerializer.Serialize(_repairItemsByProductType, new JsonSerializerOptions { WriteIndented = true });

    public IReferenceDataProvider BuildProvider(string schemaName = DefaultSchemaName)
    {
        var nodes = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        AddKeys(nodes, "product-types", JsonNode.Parse(BuildProductTypesJson()));
        AddKeys(nodes, "nrm3-codes", JsonNode.Parse(BuildNrm3CodesJson()));
        AddKeys(nodes, "repair-items-by-product-type", JsonNode.Parse(BuildRepairItemsByProductTypeJson()));

        return new InMemoryReferenceDataProvider(schemaName, nodes);
    }

    public void WriteToDirectory(string rootDirectory, string schemaName = DefaultSchemaName)
    {
        var targetDir = Path.Combine(rootDirectory, schemaName);
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(Path.Combine(targetDir, "product-types.json"), BuildProductTypesJson());
        File.WriteAllText(Path.Combine(targetDir, "nrm3-codes.json"), BuildNrm3CodesJson());
        File.WriteAllText(Path.Combine(targetDir, "repair-items-by-product-type.json"), BuildRepairItemsByProductTypeJson());
    }

    private static void AddKeys(Dictionary<string, JsonNode?> sink, string rawName, JsonNode? node)
    {
        sink[rawName] = node;
        sink[rawName.Replace('-', '_')] = node?.DeepClone();
    }

    private sealed class InMemoryReferenceDataProvider(
        string schemaName,
        IReadOnlyDictionary<string, JsonNode?> datasets) : IReferenceDataProvider
    {
        public Task<Result<IReadOnlyDictionary<string, JsonNode?>>> GetReferenceDatasetsAsync(
            SchemaMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(metadata.Name, schemaName, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Result<IReadOnlyDictionary<string, JsonNode?>>.Success(datasets));
            }

            IReadOnlyDictionary<string, JsonNode?> empty = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(Result<IReadOnlyDictionary<string, JsonNode?>>.Success(empty));
        }
    }
}
