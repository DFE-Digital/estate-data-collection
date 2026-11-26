using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Inputs;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Extensions;

public static class OdcsContractParserExtensions
{
    /// <summary>
    /// Resolves schema JSON and rules JSON from either an explicit ODCS YAML string,
    /// an inline ODCS YAML string passed in the schema field, or standard JSON inputs.
    /// </summary>
    public static Result<ResolvedContractInputs> ResolveInputs(
        this IOdcsContractParser? parser,
        string? schemaJson,
        string? rulesJson,
        string? odcsYaml,
        string? fileName = null)
    {
        if (!string.IsNullOrWhiteSpace(odcsYaml))
        {
            if (parser is null)
            {
                return Result<ResolvedContractInputs>.Success(new ResolvedContractInputs(
                    SchemaJson: schemaJson ?? odcsYaml,
                    RulesJson: rulesJson,
                    RawOdcsYaml: odcsYaml));
            }

            var parsed = parser.Parse(odcsYaml);
            if (parsed.IsFailure)
            {
                return Error.Validation(
                    ErrorCodes.SchemaVerificationFailed,
                    errors: [new ValidationError("schema", parsed.Error.Description ?? SchemaValidationMessages.InvalidOdcsYamlFallback)]);
            }

            return Result<ResolvedContractInputs>.Success(new ResolvedContractInputs(
                SchemaJson: !string.IsNullOrWhiteSpace(schemaJson) ? schemaJson : parsed.Value.GeneratedSchemaJson,
                RulesJson: !string.IsNullOrWhiteSpace(rulesJson) ? rulesJson : parsed.Value.GeneratedRulesJson,
                RawOdcsYaml: odcsYaml,
                ContractId: parsed.Value.ContractId,
                Version: parsed.Value.Version,
                ValidationPolicy: parsed.Value.ValidationPolicy));
        }

        if (parser is not null && !string.IsNullOrWhiteSpace(schemaJson) && parser.IsOdcsYaml(schemaJson, fileName))
        {
            var parsed = parser.Parse(schemaJson);
            if (parsed.IsFailure)
            {
                return Error.Validation(
                    ErrorCodes.SchemaVerificationFailed,
                    errors: [new ValidationError("schema", parsed.Error.Description ?? SchemaValidationMessages.InvalidOdcsYamlFallback)]);
            }

            return Result<ResolvedContractInputs>.Success(new ResolvedContractInputs(
                SchemaJson: parsed.Value.GeneratedSchemaJson,
                RulesJson: !string.IsNullOrWhiteSpace(rulesJson) ? rulesJson : parsed.Value.GeneratedRulesJson,
                RawOdcsYaml: schemaJson,
                ContractId: parsed.Value.ContractId,
                Version: parsed.Value.Version,
                ValidationPolicy: parsed.Value.ValidationPolicy));
        }

        return Result<ResolvedContractInputs>.Success(new ResolvedContractInputs(
            SchemaJson: schemaJson,
            RulesJson: rulesJson,
            RawOdcsYaml: null));
    }
}
