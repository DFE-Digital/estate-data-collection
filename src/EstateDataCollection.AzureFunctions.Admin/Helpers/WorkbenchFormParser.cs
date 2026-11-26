using System.Text.Json;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Microsoft.AspNetCore.Http;

namespace EstateDataCollection.AzureFunctions.Admin.Helpers;

public static class WorkbenchFormParser
{
    public static SchemaIdentifierConfig ParseIdentifierConfig(
        IFormCollection form,
        List<ValidationError>? validationErrors = null)
    {
        var identifierMode = form["identifierMode"].ToString().Trim();
        var identifierProperty = form["identifierProperty"].ToString().Trim();
        var identifierLabel = form["identifierLabel"].ToString().Trim();
        var identifierTypeRaw = form["identifierType"].ToString().Trim();
        var identifierCustomType = form["identifierCustomType"].ToString().Trim();
        var identifierPointerRaw = form["identifierPointer"].ToString().Trim();

        var isNone =
            string.Equals(identifierMode, SchemaIdentifierConfig.NoneType, StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(identifierMode) &&
             string.Equals(identifierTypeRaw, SchemaIdentifierConfig.NoneType, StringComparison.OrdinalIgnoreCase));

        if (isNone)
        {
            return new SchemaIdentifierConfig
            {
                Type = SchemaIdentifierConfig.NoneType,
                JsonPointer = null,
                Required = false
            };
        }

        string? effectivePointer;
        if (!string.IsNullOrWhiteSpace(identifierProperty))
        {
            if (string.Equals(identifierProperty, IdentifierPropertyDiscovery.CustomOptionValue, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(identifierPointerRaw))
                {
                    validationErrors?.Add(new ValidationError(
                        "identifierPointer",
                        AdminUiMessages.EnterIdentifierPointerOrNone));
                    effectivePointer = null;
                }
                else
                {
                    effectivePointer = identifierPointerRaw;
                }
            }
            else
            {
                effectivePointer = identifierProperty;
            }
        }
        else if (!string.IsNullOrWhiteSpace(identifierMode))
        {
            if (string.IsNullOrWhiteSpace(identifierPointerRaw))
            {
                validationErrors?.Add(new ValidationError(
                    "identifierPointer",
                    AdminUiMessages.EnterIdentifierPointerOrNone));
                effectivePointer = null;
            }
            else
            {
                effectivePointer = identifierPointerRaw;
            }
        }
        else
        {
            effectivePointer = string.IsNullOrWhiteSpace(identifierPointerRaw) ? null : identifierPointerRaw;
        }

        string effectiveType;
        if (!string.IsNullOrWhiteSpace(identifierLabel))
        {
            effectiveType = identifierLabel;
        }
        else if (string.Equals(identifierTypeRaw, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(identifierCustomType))
            {
                if (string.IsNullOrWhiteSpace(effectivePointer))
                {
                    validationErrors?.Add(new ValidationError(
                        "identifierCustomType",
                        AdminUiMessages.ProvideCustomIdentifierTypeOrPointer));
                }

                effectiveType = IdentifierPropertyDiscovery.DeriveTypeFromPointer(effectivePointer);
            }
            else
            {
                effectiveType = identifierCustomType;
            }
        }
        else if (!string.IsNullOrWhiteSpace(identifierTypeRaw))
        {
            effectiveType = identifierTypeRaw;
        }
        else
        {
            effectiveType = IdentifierPropertyDiscovery.DeriveTypeFromPointer(effectivePointer);
        }

        var config = new SchemaIdentifierConfig
        {
            Type = effectiveType,
            JsonPointer = effectivePointer,
            Required = null
        };

        if (validationErrors is not null)
        {
            var defCheck = config.ValidateDefinition();
            if (defCheck.IsFailure)
            {
                validationErrors.AddRange(defCheck.Error.ValidationErrors);
            }
        }

        return config;
    }

    public static Result<ParsedWorkbenchInput> Parse(IFormCollection form, bool requireSchemaIdentity)
    {
        var inputMode = form["inputMode"].ToString().Trim();
        inputMode = string.Equals(inputMode, "json", StringComparison.OrdinalIgnoreCase)
            ? "json"
            : "odcs";

        var schemaName = form["schemaName"].ToString().Trim();
        var versionRaw = form["version"].ToString().Trim();
        var fileName = form["fileName"].ToString().Trim();
        var odcsYaml = form["odcsYaml"].ToString();
        var schemaJson = form["schemaJson"].ToString();
        var rulesJson = form["rulesJson"].ToString();
        var validFixtureJson = form["validFixtureJson"].ToString();
        var invalidFixtureJson = form["invalidFixtureJson"].ToString();
        var requestedProp = form["identifierProperty"].ToString().Trim();
        var requestedLabel = form["identifierLabel"].ToString().Trim();

        var validationErrors = new List<ValidationError>();

        var version = 1;
        if (!string.IsNullOrWhiteSpace(versionRaw) && (!int.TryParse(versionRaw, out version) || version < 1))
        {
            validationErrors.Add(new ValidationError("version", AdminUiMessages.VersionMustBeAtLeastOne));
            version = 1;
        }

        if (requireSchemaIdentity && string.IsNullOrWhiteSpace(schemaName))
        {
            validationErrors.Add(new ValidationError("schemaName", AdminUiMessages.SchemaNameRequiredWhenSaving));
        }

        var validFixturesResult = ParseFixtures(validFixtureJson, "validFixtureJson");
        if (validFixturesResult.IsFailure)
        {
            validationErrors.AddRange(validFixturesResult.Error.ValidationErrors);
        }

        var invalidFixturesResult = ParseFixtures(invalidFixtureJson, "invalidFixtureJson");
        if (invalidFixturesResult.IsFailure)
        {
            validationErrors.AddRange(invalidFixturesResult.Error.ValidationErrors);
        }

        if (string.Equals(inputMode, "odcs", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(odcsYaml))
        {
            validationErrors.Add(new ValidationError("odcsYaml", AdminUiMessages.ProvideOdcsYamlContent));
        }
        else if (string.Equals(inputMode, "json", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(schemaJson))
        {
            validationErrors.Add(new ValidationError("schemaJson", AdminUiMessages.ProvideJsonSchemaContent));
        }

        var identifierConfig = ParseIdentifierConfig(form, validationErrors);

        var parsed = new ParsedWorkbenchInput(
            InputMode: inputMode,
            SchemaName: schemaName,
            Version: version,
            FileName: fileName,
            OdcsYaml: odcsYaml,
            SchemaJson: schemaJson,
            RulesJson: rulesJson,
            ValidFixtureJson: validFixtureJson,
            InvalidFixtureJson: invalidFixtureJson,
            ValidFixtures: validFixturesResult.IsSuccess ? validFixturesResult.Value : [],
            InvalidFixtures: invalidFixturesResult.IsSuccess ? invalidFixturesResult.Value : [],
            IdentifierConfig: identifierConfig,
            RequestedPropertySelection: string.IsNullOrWhiteSpace(requestedProp) ? null : requestedProp,
            RequestedIdentifierLabel: string.IsNullOrWhiteSpace(requestedLabel) ? null : requestedLabel);

        if (validationErrors.Count > 0)
        {
            return Error.Validation(
                "Workbench.InvalidForm",
                AdminUiMessages.WorkbenchFormInvalid,
                validationErrors);
        }

        return parsed;
    }

    public static ParsedWorkbenchInput ParseLenientForRedisplay(IFormCollection form)
    {
        var inputMode = string.Equals(form["inputMode"].ToString().Trim(), "json", StringComparison.OrdinalIgnoreCase)
            ? "json"
            : "odcs";
        var schemaName = form["schemaName"].ToString().Trim();
        var version = int.TryParse(form["version"].ToString().Trim(), out var v) && v >= 1 ? v : 1;
        var identifierConfig = ParseIdentifierConfig(form);
        var requestedProp = form["identifierProperty"].ToString().Trim();
        var requestedLabel = form["identifierLabel"].ToString().Trim();

        return new ParsedWorkbenchInput(
            InputMode: inputMode,
            SchemaName: schemaName,
            Version: version,
            FileName: form["fileName"].ToString().Trim(),
            OdcsYaml: form["odcsYaml"].ToString(),
            SchemaJson: form["schemaJson"].ToString(),
            RulesJson: form["rulesJson"].ToString(),
            ValidFixtureJson: form["validFixtureJson"].ToString(),
            InvalidFixtureJson: form["invalidFixtureJson"].ToString(),
            ValidFixtures: [],
            InvalidFixtures: [],
            IdentifierConfig: identifierConfig,
            RequestedPropertySelection: string.IsNullOrWhiteSpace(requestedProp) ? null : requestedProp,
            RequestedIdentifierLabel: string.IsNullOrWhiteSpace(requestedLabel) ? null : requestedLabel);
    }

    public static Result<IReadOnlyList<string>> ParseFixtures(string rawJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Result<IReadOnlyList<string>>.Success([]);
        }

        var trimmed = rawJson.Trim();
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var items = doc.RootElement
                    .EnumerateArray()
                    .Select(el => el.GetRawText())
                    .ToList();
                return Result<IReadOnlyList<string>>.Success(items);
            }

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return Result<IReadOnlyList<string>>.Success([trimmed]);
            }

            return Error.Validation(
                "Workbench.InvalidFixtureJson",
                AdminUiMessages.FixturePayloadMustBeObjectOrArray,
                [new ValidationError(fieldName, AdminUiMessages.FixtureMustBeObjectOrArrayShort)]);
        }
        catch (JsonException ex)
        {
            return Error.Validation(
                "Workbench.MalformedFixtureJson",
                ex.Message,
                [new ValidationError(fieldName, AdminUiMessages.InvalidJsonDetail(ex.Message))]);
        }
    }
}
