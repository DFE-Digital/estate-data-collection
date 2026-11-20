using System.Text.Json.Nodes;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

public interface IQualityRuleEngine : IInfrastructure
{
    /// <summary>
    /// Case-insensitive engine identifier (e.g. "jsonlogic", "cel").
    /// </summary>
    string EngineName { get; }

    /// <summary>
    /// Validates the syntax/structure of the rule's implementation during schema verification.
    /// Returns an empty list if valid, or a list of syntax errors.
    /// </summary>
    IReadOnlyList<ValidationMessage> ValidateSyntax(BusinessRule rule, string ruleId);

    /// <summary>
    /// Evaluates the rule against a resolved context node (enriched with local + root properties).
    /// Returns true if the rule passes, false if violated, or a failure Result if evaluation faults.
    /// </summary>
    Result<bool> Evaluate(BusinessRule rule, JsonNode enrichedContextNode, JsonNode rootNode);
}
