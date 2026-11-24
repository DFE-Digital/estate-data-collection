using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using YamlDotNet.Serialization;

namespace EstateDataCollection.Infrastructure.Registry;

public sealed class OdcsContractParser : IOdcsContractParser
{
    private const int MaxYamlDepth = 64;

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithAttemptingUnquotedStringTypeDeserialization()
        .WithMaximumRecursion(MaxYamlDepth)
        .Build();

    private static readonly ISerializer JsonCompatibleSerializer = new SerializerBuilder()
        .JsonCompatible()
        .Build();

    private static readonly JsonDocumentOptions ParseDocumentOptions = new()
    {
        MaxDepth = MaxYamlDepth
    };

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true
    };

    public bool IsOdcsYaml(string? content, string? fileName = null, string? contentType = null)
    {
        if (!string.IsNullOrWhiteSpace(fileName) &&
            (fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
             fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var trimmed = content.AsSpan().TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            return false;
        }

        return content.Contains("kind: DataContract", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("apiVersion:", StringComparison.OrdinalIgnoreCase);
    }

    public Result<ParsedOdcsContract> Parse(string odcsYaml, string? targetSchemaName = null)
    {
        if (string.IsNullOrWhiteSpace(odcsYaml))
        {
            return Error.Validation(ErrorCodes.PayloadEmpty, "ODCS YAML content cannot be empty.");
        }

        JsonNode? rootDoc;
        try
        {
            using var reader = new StringReader(odcsYaml);
            var yamlObject = YamlDeserializer.Deserialize(reader);
            if (yamlObject is null)
            {
                return Error.Validation(ErrorCodes.SchemaVerificationFailed, "ODCS YAML document was empty.");
            }

            var jsonText = JsonCompatibleSerializer.Serialize(yamlObject);
            rootDoc = JsonNode.Parse(jsonText, documentOptions: ParseDocumentOptions);
        }
        catch (Exception ex)
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed, $"Malformed ODCS YAML syntax: {ex.Message}");
        }

        if (rootDoc is not JsonObject contractObj)
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed, "ODCS contract root must be a YAML mapping/object.");
        }

        var contractId = GetString(contractObj, "id");
        var contractName = GetString(contractObj, "name");
        var contractVersion = GetString(contractObj, "version");

        if (contractObj["schema"] is not JsonArray schemaArray || schemaArray.Count == 0)
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed, "ODCS contract must contain a non-empty 'schema' array.");
        }

        JsonObject? selectedSchemaObj = null;
        if (!string.IsNullOrWhiteSpace(targetSchemaName))
        {
            selectedSchemaObj = schemaArray
                .OfType<JsonObject>()
                .FirstOrDefault(s => string.Equals(GetString(s, "name"), targetSchemaName, StringComparison.OrdinalIgnoreCase));
        }

        selectedSchemaObj ??= schemaArray.OfType<JsonObject>().FirstOrDefault();
        if (selectedSchemaObj is null)
        {
            return Error.Validation(ErrorCodes.SchemaVerificationFailed, "Could not resolve a target schema object in the ODCS contract.");
        }

        var extractedRules = new List<BusinessRule>();
        var jsonSchemaObj = BuildJsonSchemaObject(selectedSchemaObj, currentPath: string.Empty, isRoot: true, isNullable: false, extractedRules, depth: 0);

        var generatedSchemaJson = jsonSchemaObj.ToJsonString(IndentedJsonOptions);
        string? generatedRulesJson = null;

        if (extractedRules.Count > 0)
        {
            generatedRulesJson = JsonSerializer.Serialize(extractedRules, IndentedJsonOptions);
        }

        return new ParsedOdcsContract(
            ContractId: contractId,
            Name: contractName,
            Version: contractVersion,
            GeneratedSchemaJson: generatedSchemaJson,
            GeneratedRulesJson: generatedRulesJson,
            ExtractedRules: extractedRules);
    }

    private static JsonObject BuildJsonSchemaObject(
        JsonObject odcsNode,
        string currentPath,
        bool isRoot,
        bool isNullable,
        List<BusinessRule> extractedRules,
        int depth)
    {
        if (depth > MaxYamlDepth)
        {
            throw new InvalidOperationException($"ODCS schema nesting exceeds maximum depth of {MaxYamlDepth}.");
        }

        var schemaNode = new JsonObject();
        if (isRoot)
        {
            schemaNode["$schema"] = "http://json-schema.org/draft-07/schema#";
        }

        // Extract any quality rules attached at this node
        ExtractQualityRules(odcsNode, currentPath, extractedRules);

        var logicalType = GetString(odcsNode, "logicalType")?.ToLowerInvariant()
                          ?? GetString(odcsNode, "physicalType")?.ToLowerInvariant()
                          ?? "object";

        var baseJsonType = logicalType switch
        {
            "timestamp" or "date" or "string" => "string",
            "integer" or "int" or "long" or "bigint" => "integer",
            "number" or "float" or "double" or "decimal" => "number",
            "boolean" or "bool" => "boolean",
            "array" => "array",
            _ => "object"
        };

        if (isNullable && !isRoot)
        {
            schemaNode["type"] = new JsonArray(JsonValue.Create(baseJsonType), JsonValue.Create("null"));
        }
        else
        {
            schemaNode["type"] = baseJsonType;
        }

        if (logicalType == "timestamp")
        {
            schemaNode["format"] = "date-time";
        }
        else if (logicalType == "date")
        {
            schemaNode["format"] = "date";
        }

        if (odcsNode["logicalTypeOptions"] is JsonObject typeOpts)
        {
            if (GetString(typeOpts, "pattern") is { } pattern && !string.IsNullOrWhiteSpace(pattern))
            {
                schemaNode["pattern"] = pattern;
            }
            if (GetString(typeOpts, "format") is { } format && !string.IsNullOrWhiteSpace(format))
            {
                schemaNode["format"] = format;
            }
        }

        if (odcsNode["enum"] is JsonArray enumArr && enumArr.Count > 0)
        {
            var jsonEnum = new JsonArray();
            foreach (var item in enumArr)
            {
                if (item is JsonObject enumObj && enumObj.TryGetPropertyValue("value", out var valNode))
                {
                    jsonEnum.Add(valNode?.DeepClone());
                }
                else if (item is JsonValue enumVal)
                {
                    jsonEnum.Add(enumVal.DeepClone());
                }
            }

            if (isNullable && !jsonEnum.Any(x => x is null))
            {
                jsonEnum.Add(null);
            }

            schemaNode["enum"] = jsonEnum;
        }

        if (baseJsonType == "object" && odcsNode["properties"] is JsonArray propsArr)
        {
            var propertiesObj = new JsonObject();
            var requiredArr = new JsonArray();

            foreach (var propNode in propsArr.OfType<JsonObject>())
            {
                var propName = GetString(propNode, "name");
                if (string.IsNullOrWhiteSpace(propName))
                {
                    continue;
                }

                var isRequired = GetBool(propNode, "required") ?? false;
                if (isRequired)
                {
                    requiredArr.Add(propName);
                }

                var childPath = string.IsNullOrEmpty(currentPath) ? propName : $"{currentPath}.{propName}";
                var childSchema = BuildJsonSchemaObject(
                    propNode,
                    childPath,
                    isRoot: false,
                    isNullable: !isRequired,
                    extractedRules,
                    depth + 1);

                propertiesObj[propName] = childSchema;
            }

            schemaNode["properties"] = propertiesObj;
            schemaNode["required"] = requiredArr;
        }
        else if (baseJsonType == "array" && odcsNode["items"] is JsonObject itemsObj)
        {
            var itemRequired = GetBool(itemsObj, "required") ?? false;
            var arrayItemPath = string.IsNullOrEmpty(currentPath) ? "[]" : $"{currentPath}[]";
            schemaNode["items"] = BuildJsonSchemaObject(
                itemsObj,
                arrayItemPath,
                isRoot: false,
                isNullable: !itemRequired,
                extractedRules,
                depth + 1);
        }

        if (GetString(odcsNode, "description") is { } desc && !string.IsNullOrWhiteSpace(desc))
        {
            schemaNode["description"] = desc;
        }

        return schemaNode;
    }

    private static void ExtractQualityRules(
        JsonObject odcsNode,
        string currentPath,
        List<BusinessRule> extractedRules)
    {
        if (odcsNode["quality"] is not JsonArray qualityArr)
        {
            return;
        }

        for (var i = 0; i < qualityArr.Count; i++)
        {
            if (qualityArr[i] is not JsonObject qObj)
            {
                continue;
            }

            var id = GetString(qObj, "id") ?? $"ODCS-RULE-{extractedRules.Count + 1}";
            var type = GetString(qObj, "type") ?? "custom";
            var engine = GetString(qObj, "engine");
            var description = GetString(qObj, "description") ?? string.Empty;
            var message = GetString(qObj, "message") ?? description;
            var severity = GetString(qObj, "severity") ?? "ERROR";
            var explicitPath = GetString(qObj, "path");
            var resolvedPath = !string.IsNullOrWhiteSpace(explicitPath)
                ? explicitPath
                : (string.IsNullOrWhiteSpace(currentPath) ? "/" : currentPath);

            JsonNode? implementation = null;
            if (qObj.TryGetPropertyValue("implementation", out var implNode) && implNode is not null)
            {
                implementation = implNode.DeepClone();
            }
            else if (qObj.TryGetPropertyValue("assert", out var assertNode) && assertNode is not null)
            {
                implementation = assertNode.DeepClone();
            }

            extractedRules.Add(new BusinessRule
            {
                Id = id,
                Type = type,
                Engine = engine,
                Description = description,
                Message = message,
                Severity = severity,
                Path = resolvedPath,
                Implementation = implementation
            });
        }
    }

    private static string? GetString(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonValue val)
        {
            if (val.TryGetValue<string>(out var s))
            {
                return s;
            }
            return val.ToString();
        }
        return null;
    }

    private static bool? GetBool(JsonObject obj, string propertyName)
    {
        if (obj.TryGetPropertyValue(propertyName, out var node) && node is JsonValue val)
        {
            if (val.TryGetValue<bool>(out var b))
            {
                return b;
            }
            if (val.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }
}
