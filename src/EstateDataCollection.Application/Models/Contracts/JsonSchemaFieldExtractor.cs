using System.Text.Json.Nodes;

namespace EstateDataCollection.Application.Models.Contracts;

/// <summary>
/// Extracts a hierarchical list of <see cref="SchemaFieldDescriptor"/> items from a JSON Schema (Draft-07 or Draft 2020-12),
/// resolving local $ref pointers (#/$defs/... or #/definitions/...) and combinators (allOf/anyOf/oneOf).
/// </summary>
public static class JsonSchemaFieldExtractor
{
    private const int MaxDepth = 12;

    public static IReadOnlyList<SchemaFieldDescriptor> ExtractFields(string? rawJsonSchema)
    {
        if (string.IsNullOrWhiteSpace(rawJsonSchema))
        {
            return [];
        }

        try
        {
            if (JsonNode.Parse(rawJsonSchema) is not JsonObject root)
            {
                return [];
            }

            var visitedRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return ExtractObjectProperties(root, root, parentPath: string.Empty, depth: 0, visitedRefs);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<SchemaFieldDescriptor> ExtractObjectProperties(
        JsonObject schemaObj,
        JsonObject rootSchema,
        string parentPath,
        int depth,
        HashSet<string> visitedRefs)
    {
        if (depth > MaxDepth)
        {
            return [];
        }

        var effectiveObj = ResolveEffectiveObject(schemaObj, rootSchema, visitedRefs);
        var requiredSet = new HashSet<string>(StringComparer.Ordinal);

        CollectRequiredFields(effectiveObj, requiredSet);

        var fields = new List<SchemaFieldDescriptor>();
        if (effectiveObj.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject propsObj)
        {
            foreach (var (propName, propSchemaNode) in propsObj)
            {
                if (propSchemaNode is not JsonObject propSchemaObj)
                {
                    continue;
                }

                var fieldPath = string.IsNullOrEmpty(parentPath) ? propName : $"{parentPath}.{propName}";
                var descriptor = BuildFieldDescriptor(
                    propName,
                    fieldPath,
                    propSchemaObj,
                    rootSchema,
                    isRequired: requiredSet.Contains(propName),
                    depth: depth + 1,
                    new HashSet<string>(visitedRefs, StringComparer.OrdinalIgnoreCase));

                fields.Add(descriptor);
            }
        }

        return fields;
    }

    private static SchemaFieldDescriptor BuildFieldDescriptor(
        string name,
        string path,
        JsonObject propSchemaObj,
        JsonObject rootSchema,
        bool isRequired,
        int depth,
        HashSet<string> visitedRefs)
    {
        var effective = ResolveEffectiveObject(propSchemaObj, rootSchema, visitedRefs);

        var type = ExtractSchemaType(effective);
        var title = TryGetString(effective, "title") ?? TryGetString(propSchemaObj, "title");
        var description = TryGetString(effective, "description") ?? TryGetString(propSchemaObj, "description");
        var format = TryGetString(effective, "format");
        var constraints = ExtractConstraints(effective);

        IReadOnlyList<SchemaFieldDescriptor>? children = null;

        if (string.Equals(type, "array", StringComparison.OrdinalIgnoreCase) ||
            effective.ContainsKey("items"))
        {
            if (effective.TryGetPropertyValue("items", out var itemsNode) && itemsNode is JsonObject itemsObj)
            {
                var resolvedItems = ResolveEffectiveObject(itemsObj, rootSchema, visitedRefs);
                var itemType = ExtractSchemaType(resolvedItems);
                var arrayItemPath = $"{path}[]";

                if (string.Equals(itemType, "object", StringComparison.OrdinalIgnoreCase) ||
                    resolvedItems.ContainsKey("properties"))
                {
                    var itemChildren = ExtractObjectProperties(resolvedItems, rootSchema, arrayItemPath, depth + 1, visitedRefs);
                    if (itemChildren.Count > 0)
                    {
                        children = itemChildren;
                    }
                }
            }
        }
        else if (string.Equals(type, "object", StringComparison.OrdinalIgnoreCase) ||
                 effective.ContainsKey("properties"))
        {
            var nestedChildren = ExtractObjectProperties(effective, rootSchema, path, depth + 1, visitedRefs);
            if (nestedChildren.Count > 0)
            {
                children = nestedChildren;
            }
        }

        return new SchemaFieldDescriptor(
            Name: name,
            Path: path,
            Type: type,
            Required: isRequired,
            Title: title,
            Description: description,
            Format: format,
            Constraints: constraints,
            Children: children);
    }

    private static JsonObject ResolveEffectiveObject(
        JsonObject current,
        JsonObject rootSchema,
        HashSet<string> visitedRefs)
    {
        var merged = new JsonObject();

        // 1. Follow local $ref if present
        if (current.TryGetPropertyValue("$ref", out var refNode) &&
            refNode is JsonValue refVal &&
            refVal.TryGetValue<string>(out var refString) &&
            !string.IsNullOrWhiteSpace(refString) &&
            visitedRefs.Add(refString))
        {
            var target = ResolveLocalRef(rootSchema, refString);
            if (target is not null)
            {
                var resolvedTarget = ResolveEffectiveObject(target, rootSchema, visitedRefs);
                MergeInto(merged, resolvedTarget);
            }
        }

        // 2. Merge allOf / anyOf / oneOf object branches
        foreach (var combinator in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (current.TryGetPropertyValue(combinator, out var combNode) && combNode is JsonArray combArr)
            {
                foreach (var item in combArr)
                {
                    if (item is JsonObject branchObj)
                    {
                        var resolvedBranch = ResolveEffectiveObject(branchObj, rootSchema, visitedRefs);
                        MergeInto(merged, resolvedBranch);
                    }
                }
            }
        }

        // 3. Overlay direct properties of current object
        MergeInto(merged, current);
        return merged;
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (key == "$ref" || value is null)
            {
                continue;
            }

            if (key == "properties" && value is JsonObject srcProps)
            {
                if (!target.TryGetPropertyValue("properties", out var tgtPropsNode) || tgtPropsNode is not JsonObject tgtProps)
                {
                    tgtProps = new JsonObject();
                    target["properties"] = tgtProps;
                }

                foreach (var (pName, pVal) in srcProps)
                {
                    if (pVal is not null)
                    {
                        tgtProps[pName] = pVal.DeepClone();
                    }
                }
            }
            else if (key == "required" && value is JsonArray srcReq)
            {
                if (!target.TryGetPropertyValue("required", out var tgtReqNode) || tgtReqNode is not JsonArray tgtReq)
                {
                    tgtReq = new JsonArray();
                    target["required"] = tgtReq;
                }

                var existing = tgtReq
                    .Select(x => x?.GetValue<string>())
                    .Where(x => x is not null)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var reqItem in srcReq)
                {
                    if (reqItem is JsonValue v && v.TryGetValue<string>(out var s) && existing.Add(s))
                    {
                        tgtReq.Add(s);
                    }
                }
            }
            else
            {
                target[key] = value.DeepClone();
            }
        }
    }

    private static JsonObject? ResolveLocalRef(JsonObject rootSchema, string refPointer)
    {
        if (!refPointer.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }

        var segments = refPointer[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = rootSchema;

        foreach (var rawSegment in segments)
        {
            var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var next))
            {
                current = next;
            }
            else
            {
                return null;
            }
        }

        return current as JsonObject;
    }

    private static void CollectRequiredFields(JsonObject schemaObj, HashSet<string> requiredSet)
    {
        if (schemaObj.TryGetPropertyValue("required", out var reqNode) && reqNode is JsonArray reqArray)
        {
            foreach (var item in reqArray)
            {
                if (item is JsonValue val && val.TryGetValue<string>(out var propName) && !string.IsNullOrWhiteSpace(propName))
                {
                    requiredSet.Add(propName);
                }
            }
        }
    }

    private static string ExtractSchemaType(JsonObject schemaObj)
    {
        if (schemaObj.TryGetPropertyValue("type", out var typeNode))
        {
            if (typeNode is JsonValue typeVal && typeVal.TryGetValue<string>(out var singleType) && !string.IsNullOrWhiteSpace(singleType))
            {
                return singleType;
            }

            if (typeNode is JsonArray typeArr)
            {
                var nonNullTypes = typeArr
                    .Select(t => t is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "null", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (nonNullTypes.Count > 0)
                {
                    return string.Join("|", nonNullTypes);
                }
            }
        }

        if (schemaObj.ContainsKey("properties"))
        {
            return "object";
        }

        if (schemaObj.ContainsKey("items"))
        {
            return "array";
        }

        if (schemaObj.ContainsKey("enum"))
        {
            return "string";
        }

        return "any";
    }

    private static SchemaFieldConstraints? ExtractConstraints(JsonObject schemaObj)
    {
        var min = TryGetDecimal(schemaObj, "minimum") ?? TryGetDecimal(schemaObj, "exclusiveMinimum");
        var max = TryGetDecimal(schemaObj, "maximum") ?? TryGetDecimal(schemaObj, "exclusiveMaximum");
        var minLength = TryGetInt(schemaObj, "minLength");
        var maxLength = TryGetInt(schemaObj, "maxLength");
        var pattern = TryGetString(schemaObj, "pattern");

        List<string>? enumValues = null;
        if (schemaObj.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumArr)
        {
            enumValues = enumArr
                .Where(x => x is not null)
                .Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : x!.ToJsonString())
                .ToList();
        }
        else if (schemaObj.TryGetPropertyValue("const", out var constNode) && constNode is not null)
        {
            var constVal = constNode is JsonValue v && v.TryGetValue<string>(out var s) ? s : constNode.ToJsonString();
            enumValues = [constVal];
        }

        if (min is null && max is null && minLength is null && maxLength is null && pattern is null && enumValues is null)
        {
            return null;
        }

        return new SchemaFieldConstraints(
            Minimum: min,
            Maximum: max,
            MinLength: minLength,
            MaxLength: maxLength,
            Pattern: pattern,
            EnumValues: enumValues);
    }

    private static string? TryGetString(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) && node is JsonValue val && val.TryGetValue<string>(out var s)
            ? s
            : null;
    }

    private static decimal? TryGetDecimal(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) && node is JsonValue val && val.TryGetValue<decimal>(out var d)
            ? d
            : null;
    }

    private static int? TryGetInt(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) && node is JsonValue val && val.TryGetValue<int>(out var i)
            ? i
            : null;
    }
}
