using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Validators;

/// <summary>
/// Validates a <see cref="SchemaIdentifierConfig"/> (or platform default paths) against a JSON Schema AST
/// to ensure the configured identifier pointer exists, targets a scalar value (string/integer/number),
/// and derives requiredness directly from the schema's 'required' declaration.
/// </summary>
public static class IdentifierSchemaValidator
{
    private const int MaxDepth = 12;

    public static IdentifierSchemaAlignmentResult ValidateAndAlign(
        string? rawJsonSchema,
        SchemaIdentifierConfig? requestedConfig,
        IReadOnlyList<string> defaultPaths,
        bool requireDefaultIdentifier = true)
    {
        SchemaIdentifierConfig? normalized = null;
        if (requestedConfig is not null)
        {
            var defValidation = requestedConfig.ValidateDefinition();
            if (defValidation.IsFailure)
            {
                var msgs = defValidation.Error.ValidationErrors.Count > 0
                    ? defValidation.Error.ValidationErrors
                        .Select(e => new ValidationMessage(typeof(IdentifierSchemaValidator), e.PropertyPath, e.Message, source: ValidationSources.Identifier))
                        .ToList()
                    : [new ValidationMessage(typeof(IdentifierSchemaValidator), "identifierConfig", defValidation.Error.Description ?? DomainMessages.InvalidIdentifierConfigFallback, source: ValidationSources.Identifier)];

                return new IdentifierSchemaAlignmentResult(
                    IsValid: false,
                    IsEnabled: !requestedConfig.IsDisabled,
                    EffectiveConfig: null,
                    ResolvedPaths: [],
                    IsRequiredInSchema: false,
                    Errors: msgs);
            }

            normalized = requestedConfig.Normalise();

            if (normalized.IsDisabled)
            {
                return new IdentifierSchemaAlignmentResult(
                    IsValid: true,
                    IsEnabled: false,
                    EffectiveConfig: normalized,
                    ResolvedPaths: [],
                    IsRequiredInSchema: false,
                    Errors: []);
            }
        }

        var fallbackPaths = defaultPaths is { Count: > 0 } ? defaultPaths : [];

        // Rules-only verification (no JSON schema in this payload): validate definition only.
        if (string.IsNullOrWhiteSpace(rawJsonSchema))
        {
            var paths = normalized?.JsonPointer is { Length: > 0 } ptr
                ? [ptr]
                : fallbackPaths;

            var enabled = normalized is not null || requireDefaultIdentifier;
            return new IdentifierSchemaAlignmentResult(
                IsValid: true,
                IsEnabled: enabled,
                EffectiveConfig: normalized,
                ResolvedPaths: paths,
                IsRequiredInSchema: normalized?.Required ?? requireDefaultIdentifier,
                Errors: []);
        }

        JsonObject? rootObj;
        try
        {
            rootObj = JsonNode.Parse(rawJsonSchema) as JsonObject;
        }
        catch
        {
            rootObj = null;
        }

        // Malformed schema JSON is already reported by Phase One A syntax validation.
        if (rootObj is null)
        {
            return new IdentifierSchemaAlignmentResult(
                IsValid: true,
                IsEnabled: false,
                EffectiveConfig: normalized,
                ResolvedPaths: [],
                IsRequiredInSchema: false,
                Errors: []);
        }

        // Case A: Explicit JsonPointer configured (e.g. "/school/urn", "/establishment/ukprn", "/laestab")
        if (normalized is not null && !string.IsNullOrWhiteSpace(normalized.JsonPointer))
        {
            var inspection = InspectPathInSchema(rootObj, normalized.JsonPointer);
            if (!inspection.Exists)
            {
                return new IdentifierSchemaAlignmentResult(
                    IsValid: false,
                    IsEnabled: true,
                    EffectiveConfig: null,
                    ResolvedPaths: [],
                    IsRequiredInSchema: false,
                    Errors:
                    [
                        new ValidationMessage(
                            typeof(IdentifierSchemaValidator),
                            "identifierConfig.jsonPointer",
                            inspection.FailureReason ?? SchemaValidationMessages.IdentifierPointerNotFound(normalized.JsonPointer),
                            source: ValidationSources.Identifier,
                            path: normalized.JsonPointer)
                    ]);
            }

            if (!inspection.IsScalarCompatible)
            {
                return new IdentifierSchemaAlignmentResult(
                    IsValid: false,
                    IsEnabled: true,
                    EffectiveConfig: null,
                    ResolvedPaths: [],
                    IsRequiredInSchema: false,
                    Errors:
                    [
                        new ValidationMessage(
                            typeof(IdentifierSchemaValidator),
                            "identifierConfig.jsonPointer",
                            inspection.FailureReason ?? SchemaValidationMessages.IdentifierPointerMustBeScalar(normalized.JsonPointer),
                            source: ValidationSources.Identifier,
                            path: normalized.JsonPointer)
                    ]);
            }

            var effectiveRequired = inspection.IsRequiredInSchema && !inspection.AllowsNull;
            var resolvedPointer = inspection.NormalizedPointer ?? normalized.JsonPointer;
            var alignedConfig = normalized with
            {
                JsonPointer = resolvedPointer,
                Required = effectiveRequired
            };

            return new IdentifierSchemaAlignmentResult(
                IsValid: true,
                IsEnabled: true,
                EffectiveConfig: alignedConfig,
                ResolvedPaths: [resolvedPointer],
                IsRequiredInSchema: effectiveRequired,
                Errors: []);
        }

        // Case B: Enabled without an explicit pointer, or null config -> search defaultPaths (e.g. /school/urn, /urn)
        foreach (var candidatePath in fallbackPaths)
        {
            var inspection = InspectPathInSchema(rootObj, candidatePath);
            if (inspection.Exists && inspection.IsScalarCompatible && inspection.NormalizedPointer is not null)
            {
                var effectiveRequired = inspection.IsRequiredInSchema && !inspection.AllowsNull;
                var alignedConfig = normalized is null
                    ? null
                    : normalized with
                    {
                        JsonPointer = inspection.NormalizedPointer,
                        Required = effectiveRequired
                    };

                return new IdentifierSchemaAlignmentResult(
                    IsValid: true,
                    IsEnabled: true,
                    EffectiveConfig: alignedConfig,
                    ResolvedPaths: [inspection.NormalizedPointer],
                    IsRequiredInSchema: effectiveRequired,
                    Errors: []);
            }
        }

        // If null config and platform does not require a default identifier, treat as disabled.
        if (normalized is null && !requireDefaultIdentifier)
        {
            return new IdentifierSchemaAlignmentResult(
                IsValid: true,
                IsEnabled: false,
                EffectiveConfig: null,
                ResolvedPaths: [],
                IsRequiredInSchema: false,
                Errors: []);
        }

        var searchedFormatted = string.Join(", ", fallbackPaths.Select(p =>
            SchemaIdentifierConfig.TryParsePointerSegments(p, out var segs)
                ? SchemaIdentifierConfig.ToJsonPointer(segs)
                : p));

        return new IdentifierSchemaAlignmentResult(
            IsValid: false,
            IsEnabled: true,
            EffectiveConfig: null,
            ResolvedPaths: [],
            IsRequiredInSchema: false,
            Errors:
            [
                new ValidationMessage(
                    typeof(IdentifierSchemaValidator),
                    "identifierConfig.jsonPointer",
                    SchemaValidationMessages.SchemaMissingDefaultIdentifierPath(searchedFormatted),
                    source: ValidationSources.Identifier)
            ]);
    }

    public static bool? TryResolveRequiredFromSchema(string? rawJsonSchema, IReadOnlyList<string> paths)
    {
        if (string.IsNullOrWhiteSpace(rawJsonSchema) || paths.Count == 0)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(rawJsonSchema) is not JsonObject rootObj)
            {
                return null;
            }

            foreach (var path in paths)
            {
                var inspection = InspectPathInSchema(rootObj, path);
                if (inspection.Exists)
                {
                    return inspection.IsRequiredInSchema && !inspection.AllowsNull;
                }
            }
        }
        catch
        {
            // Ignore parse errors and allow caller fallback
        }

        return null;
    }

    public static SchemaIdentifierPathInspection InspectPathInSchema(JsonObject rootSchema, string rawPath)
    {
        if (!SchemaIdentifierConfig.TryParsePointerSegments(rawPath, out var segments))
        {
            return new SchemaIdentifierPathInspection(
                Exists: false,
                IsScalarCompatible: false,
                IsRequiredInSchema: false,
                AllowsNull: false,
                NormalizedPointer: null,
                FailureReason: SchemaValidationMessages.IdentifierPointerInvalidSyntax(rawPath));
        }

        var normalizedPointer = SchemaIdentifierConfig.ToJsonPointer(segments);
        var current = rootSchema;
        var allRequired = true;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var resolved = ResolveEffectiveObject(current, rootSchema, new HashSet<string>(StringComparer.OrdinalIgnoreCase), depth: 0);

            if (!TryGetPropertySchema(resolved, segment, out var matchedName, out var nextSchemaObj))
            {
                return new SchemaIdentifierPathInspection(
                    Exists: false,
                    IsScalarCompatible: false,
                    IsRequiredInSchema: false,
                    AllowsNull: false,
                    NormalizedPointer: normalizedPointer,
                    FailureReason: SchemaValidationMessages.PropertyNotDeclaredInSchema(segment, normalizedPointer));
            }

            if (!IsPropertyInRequiredList(resolved, matchedName))
            {
                allRequired = false;
            }

            current = nextSchemaObj!;
        }

        var leaf = ResolveEffectiveObject(current, rootSchema, new HashSet<string>(StringComparer.OrdinalIgnoreCase), depth: 0);
        var (isScalar, allowsNull, reason) = EvaluateScalarCompatibility(leaf, rootSchema, normalizedPointer);

        return new SchemaIdentifierPathInspection(
            Exists: true,
            IsScalarCompatible: isScalar,
            IsRequiredInSchema: allRequired,
            AllowsNull: allowsNull,
            NormalizedPointer: normalizedPointer,
            FailureReason: reason);
    }

    private static (bool IsScalar, bool AllowsNull, string? Reason) EvaluateScalarCompatibility(
        JsonObject leafSchema,
        JsonObject rootSchema,
        string normalizedPointer)
    {
        var typeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectSchemaTypes(leafSchema, rootSchema, typeSet, new HashSet<string>(StringComparer.OrdinalIgnoreCase), depth: 0);

        var allowsNull = typeSet.Contains("null");

        if (typeSet.Count == 0)
        {
            // Untyped subschema (`{}`) or enum/const without explicit `type`
            if (leafSchema.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumArr)
            {
                var hasScalar = false;
                foreach (var item in enumArr)
                {
                    if (item is null)
                    {
                        allowsNull = true;
                    }
                    else if (item is JsonValue v)
                    {
                        var kind = v.GetValueKind();
                        if (kind is JsonValueKind.String or JsonValueKind.Number)
                        {
                            hasScalar = true;
                        }
                    }
                }

                if (hasScalar)
                {
                    return (true, allowsNull, null);
                }

                return (false, allowsNull, SchemaValidationMessages.IdentifierEnumMustBeScalar(normalizedPointer));
            }

            return (false, allowsNull, SchemaValidationMessages.IdentifierMustDeclareScalarType(normalizedPointer));
        }

        var hasCompatibleScalar = typeSet.Contains("string") || typeSet.Contains("integer") || typeSet.Contains("number");
        if (!hasCompatibleScalar)
        {
            var foundTypes = string.Join(", ", typeSet.OrderBy(t => t, StringComparer.Ordinal));
            return (
                false,
                allowsNull,
                SchemaValidationMessages.IdentifierIncompatibleType(normalizedPointer, foundTypes));
        }

        return (true, allowsNull, null);
    }

    private static void CollectSchemaTypes(
        JsonObject schemaObj,
        JsonObject rootSchema,
        HashSet<string> types,
        HashSet<string> visitedRefs,
        int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        var effective = ResolveEffectiveObject(schemaObj, rootSchema, visitedRefs, depth);

        if (effective.TryGetPropertyValue("type", out var typeNode))
        {
            if (typeNode is JsonValue tv && tv.TryGetValue<string>(out var single) && !string.IsNullOrWhiteSpace(single))
            {
                types.Add(single);
            }
            else if (typeNode is JsonArray ta)
            {
                foreach (var item in ta)
                {
                    if (item is JsonValue av && av.TryGetValue<string>(out var t) && !string.IsNullOrWhiteSpace(t))
                    {
                        types.Add(t);
                    }
                }
            }
        }

        if (effective.ContainsKey("properties"))
        {
            types.Add("object");
        }

        if (effective.ContainsKey("items"))
        {
            types.Add("array");
        }

        foreach (var combinator in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (effective.TryGetPropertyValue(combinator, out var combNode) && combNode is JsonArray combArr)
            {
                foreach (var branch in combArr.OfType<JsonObject>())
                {
                    CollectSchemaTypes(branch, rootSchema, types, visitedRefs, depth + 1);
                }
            }
        }
    }

    private static JsonObject ResolveEffectiveObject(
        JsonObject current,
        JsonObject rootSchema,
        HashSet<string> visitedRefs,
        int depth)
    {
        if (depth > MaxDepth)
        {
            return current;
        }

        var merged = new JsonObject();

        if (current.TryGetPropertyValue("$ref", out var refNode) &&
            refNode is JsonValue refVal &&
            refVal.TryGetValue<string>(out var refString) &&
            !string.IsNullOrWhiteSpace(refString) &&
            visitedRefs.Add(refString))
        {
            var target = ResolveLocalRef(rootSchema, refString);
            if (target is not null)
            {
                var resolvedTarget = ResolveEffectiveObject(target, rootSchema, visitedRefs, depth + 1);
                MergeInto(merged, resolvedTarget, mergeRequired: true);
            }
        }

        if (current.TryGetPropertyValue("allOf", out var allOfNode) && allOfNode is JsonArray allOfArr)
        {
            foreach (var branchObj in allOfArr.OfType<JsonObject>())
            {
                var resolvedBranch = ResolveEffectiveObject(branchObj, rootSchema, visitedRefs, depth + 1);
                MergeInto(merged, resolvedBranch, mergeRequired: true);
            }
        }

        foreach (var combinator in new[] { "anyOf", "oneOf" })
        {
            if (current.TryGetPropertyValue(combinator, out var combNode) && combNode is JsonArray combArr)
            {
                foreach (var branchObj in combArr.OfType<JsonObject>())
                {
                    var resolvedBranch = ResolveEffectiveObject(branchObj, rootSchema, visitedRefs, depth + 1);
                    MergeInto(merged, resolvedBranch, mergeRequired: false);
                }
            }
        }

        MergeInto(merged, current, mergeRequired: true);
        return merged;
    }

    private static void MergeInto(JsonObject target, JsonObject source, bool mergeRequired)
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
                if (!mergeRequired)
                {
                    continue;
                }

                if (!target.TryGetPropertyValue("required", out var tgtReqNode) || tgtReqNode is not JsonArray tgtReq)
                {
                    tgtReq = new JsonArray();
                    target["required"] = tgtReq;
                }

                var existing = tgtReq
                    .Select(x => x is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null)
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

    private static bool TryGetPropertySchema(
        JsonObject schemaObj,
        string targetProperty,
        out string matchedName,
        out JsonObject? propertySchema)
    {
        matchedName = targetProperty;
        propertySchema = null;

        if (!schemaObj.TryGetPropertyValue("properties", out var propsNode) || propsNode is not JsonObject propsObj)
        {
            return false;
        }

        if (propsObj.TryGetPropertyValue(targetProperty, out var exactNode) && exactNode is JsonObject exactObj)
        {
            matchedName = targetProperty;
            propertySchema = exactObj;
            return true;
        }

        foreach (var (key, value) in propsObj)
        {
            if (string.Equals(key, targetProperty, StringComparison.OrdinalIgnoreCase) && value is JsonObject ciObj)
            {
                matchedName = key;
                propertySchema = ciObj;
                return true;
            }
        }

        return false;
    }

    private static bool IsPropertyInRequiredList(JsonObject schemaObj, string propertyName)
    {
        if (!schemaObj.TryGetPropertyValue("required", out var reqNode) || reqNode is not JsonArray reqArray)
        {
            return false;
        }

        foreach (var item in reqArray)
        {
            if (item is JsonValue val &&
                val.TryGetValue<string>(out var reqName) &&
                string.Equals(reqName, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
