using System.Text.Json.Nodes;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

public interface IBusinessLogicValidator : IInfrastructure
{
    IReadOnlyList<ValidationEngineDescriptor> GetRegisteredEngines() => [];

    Result<IReadOnlyList<ValidationMessage>> ValidateRulesSyntax(
        string rawRulesJson,
        out IReadOnlyList<BusinessRule>? parsedRules);

    Task<Result<BusinessRuleValidationResult>> ValidateAsync(
        string payloadJson,
        IReadOnlyList<BusinessRule> rules,
        CancellationToken cancellationToken = default);

    Task<Result<BusinessRuleValidationResult>> ValidateAsync(
        string payloadJson,
        IReadOnlyList<BusinessRule> rules,
        IReadOnlyDictionary<string, JsonNode?>? additionalAmbientContext,
        double? numericToleranceOverride = null,
        CancellationToken cancellationToken = default)
        => ValidateAsync(payloadJson, rules, cancellationToken);
}
