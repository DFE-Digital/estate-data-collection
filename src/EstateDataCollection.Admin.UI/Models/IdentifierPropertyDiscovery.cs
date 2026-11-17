using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Admin.UI.Models;

/// <summary>
/// Discovers scalar candidate identifier properties from a schema's field descriptors,
/// raw JSON Schema, or ODCS YAML, and derives canonical partition labels from JSON Pointers.
/// </summary>
public static class IdentifierPropertyDiscovery
{
    public const string CustomOptionValue = "__custom__";

    private static readonly HashSet<string> ScalarTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string",
        "integer",
        "number",
        "date",
        "timestamp",
        "decimal",
        "float",
        "double",
        "long",
        "int"
    };

    public static string? DeriveTypeFromPointerOrNull(string? jsonPointer)
    {
        if (string.IsNullOrWhiteSpace(jsonPointer) ||
            !SchemaIdentifierConfig.TryParsePointerSegments(jsonPointer, out var segments) ||
            segments.Count == 0)
        {
            return null;
        }

        var leaf = segments[^1].Trim();
        if (string.Equals(leaf, "urn", StringComparison.OrdinalIgnoreCase))
        {
            return "URN";
        }

        if (string.Equals(leaf, "ukprn", StringComparison.OrdinalIgnoreCase))
        {
            return "UKPRN";
        }

        if (string.Equals(leaf, "laestab", StringComparison.OrdinalIgnoreCase))
        {
            return "LAEstab";
        }

        var sanitized = new string(leaf
            .Where(c => !char.IsWhiteSpace(c) && c != '/' && c != '\\')
            .ToArray())
            .Replace("..", string.Empty, StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    public static string DeriveTypeFromPointer(string? jsonPointer, string? fallbackType = null)
    {
        var derived = DeriveTypeFromPointerOrNull(jsonPointer);
        if (derived is not null)
        {
            return derived;
        }

        if (!string.IsNullOrWhiteSpace(fallbackType) &&
            !string.Equals(fallbackType, SchemaIdentifierConfig.NoneType, StringComparison.OrdinalIgnoreCase))
        {
            return fallbackType.Trim();
        }

        return SchemaIdentifierConfig.DefaultUrnType;
    }

    public static IReadOnlyList<SchemaIdentifierPropertyOption> FromFieldDescriptors(
        IReadOnlyList<SchemaFieldDescriptor>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return [];
        }

        var results = new List<SchemaIdentifierPropertyOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectScalarOptions(fields, parentRequired: true, results, seen);
        return results;
    }

    public static IReadOnlyList<SchemaIdentifierPropertyOption> FromRawInputs(
        string? inputMode,
        string? rawSchemaJson,
        string? rawOdcsYaml)
    {
        if (!string.Equals(inputMode, "json", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(rawOdcsYaml))
        {
            var fromOdcs = FromOdcsYaml(rawOdcsYaml);
            if (fromOdcs.Count > 0)
            {
                return fromOdcs;
            }
        }

        if (!string.IsNullOrWhiteSpace(rawSchemaJson))
        {
            return FromFieldDescriptors(JsonSchemaFieldExtractor.ExtractFields(rawSchemaJson));
        }

        return [];
    }

    private static void CollectScalarOptions(
        IReadOnlyList<SchemaFieldDescriptor> fields,
        bool parentRequired,
        List<SchemaIdentifierPropertyOption> results,
        HashSet<string> seen)
    {
        foreach (var field in fields)
        {
            if (field.Path.Contains("[]", StringComparison.Ordinal))
            {
                continue;
            }

            var effectiveRequired = parentRequired && field.Required;

            if (field.Children is { Count: > 0 })
            {
                CollectScalarOptions(field.Children, effectiveRequired, results, seen);
                continue;
            }

            if (!IsScalarCompatible(field.Type))
            {
                continue;
            }

            if (!SchemaIdentifierConfig.TryParsePointerSegments(field.Path, out var segments) || segments.Count == 0)
            {
                continue;
            }

            var pointer = SchemaIdentifierConfig.ToJsonPointer(segments);
            if (!seen.Add(pointer))
            {
                continue;
            }

            results.Add(new SchemaIdentifierPropertyOption(
                Pointer: pointer,
                Name: field.Name,
                ValueType: string.IsNullOrWhiteSpace(field.Type) ? "string" : field.Type,
                Required: effectiveRequired,
                DerivedType: DeriveTypeFromPointer(pointer)));
        }
    }

    private static bool IsScalarCompatible(string? fieldType)
    {
        if (string.IsNullOrWhiteSpace(fieldType))
        {
            return false;
        }

        foreach (var part in fieldType.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ScalarTypes.Contains(part))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<SchemaIdentifierPropertyOption> FromOdcsYaml(string odcsYaml)
    {
        var results = new List<SchemaIdentifierPropertyOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lines = odcsYaml.Replace("\r\n", "\n").Split('\n');
        var inSchemaSection = false;
        var schemaIndent = -1;

        var nodes = new List<(int Indent, string Name, string? LogicalType, bool Required, bool HasProperties)>();
        int? activeNodeIdx = null;
        var inRootObjectHeader = false;
        var rootPropsIndent = -1;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var indent = rawLine.TakeWhile(c => c == ' ').Count();

            if (!inSchemaSection)
            {
                if (trimmed == "schema:" || trimmed.StartsWith("schema:", StringComparison.Ordinal))
                {
                    inSchemaSection = true;
                    schemaIndent = indent;
                }
                continue;
            }

            if (indent <= schemaIndent && !trimmed.StartsWith('-'))
            {
                break;
            }

            if (trimmed.StartsWith("- name:", StringComparison.Ordinal))
            {
                var nameVal = UnquoteYaml(trimmed[7..]);
                if (rootPropsIndent < 0 && indent <= schemaIndent + 4)
                {
                    inRootObjectHeader = true;
                    activeNodeIdx = null;
                }
                else
                {
                    inRootObjectHeader = false;
                    nodes.Add((indent, nameVal, null, false, false));
                    activeNodeIdx = nodes.Count - 1;
                }
                continue;
            }

            if (inRootObjectHeader)
            {
                if (trimmed.StartsWith("properties:", StringComparison.Ordinal))
                {
                    rootPropsIndent = indent;
                    inRootObjectHeader = false;
                }
                continue;
            }

            if (activeNodeIdx.HasValue)
            {
                var idx = activeNodeIdx.Value;
                var cur = nodes[idx];

                if (trimmed.StartsWith("logicalType:", StringComparison.Ordinal))
                {
                    nodes[idx] = cur with { LogicalType = UnquoteYaml(trimmed[12..]) };
                }
                else if (trimmed.StartsWith("required:", StringComparison.Ordinal))
                {
                    var reqVal = UnquoteYaml(trimmed[9..]);
                    nodes[idx] = cur with { Required = string.Equals(reqVal, "true", StringComparison.OrdinalIgnoreCase) };
                }
                else if (trimmed.StartsWith("properties:", StringComparison.Ordinal))
                {
                    nodes[idx] = cur with { HasProperties = true };
                }
            }
        }

        var stack = new List<(int Indent, string Name, bool Required, bool IsArray)>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            while (stack.Count > 0 && stack[^1].Indent >= node.Indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var parentIsArray = stack.Any(s => s.IsArray);
            var isArray = string.Equals(node.LogicalType, "array", StringComparison.OrdinalIgnoreCase);
            var isObject = node.HasProperties || string.Equals(node.LogicalType, "object", StringComparison.OrdinalIgnoreCase);
            var hasNestedChildren = i + 1 < nodes.Count && nodes[i + 1].Indent > node.Indent;

            stack.Add((node.Indent, node.Name, node.Required, isArray));

            if (parentIsArray || isArray || isObject || hasNestedChildren)
            {
                continue;
            }

            var valueType = string.IsNullOrWhiteSpace(node.LogicalType) ? "string" : node.LogicalType!;
            if (!IsScalarCompatible(valueType))
            {
                continue;
            }

            var segments = stack.Select(s => s.Name).ToArray();
            var pointer = SchemaIdentifierConfig.ToJsonPointer(segments);
            if (!seen.Add(pointer))
            {
                continue;
            }

            var allRequired = stack.All(s => s.Required);
            results.Add(new SchemaIdentifierPropertyOption(
                Pointer: pointer,
                Name: node.Name,
                ValueType: valueType,
                Required: allRequired,
                DerivedType: DeriveTypeFromPointer(pointer)));
        }

        return results;
    }

    private static string UnquoteYaml(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) ||
             (trimmed.StartsWith('\'') && trimmed.EndsWith('\''))))
        {
            return trimmed[1..^1].Trim();
        }
        return trimmed;
    }
}
