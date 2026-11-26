using System.Text.Json.Nodes;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Markers;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

public interface IQualityRuleEngine : IInfrastructure
{
    /// <summary>
    /// Case-insensitive engine identifier (e.g. "jsonlogic", "cel", "unique").
    /// </summary>
    string EngineName { get; }

    /// <summary>
    /// Declarative metadata describing this engine's identity.
    /// </summary>
    WorkflowEngineDescriptor Descriptor =>
        new(
            EngineName,
            EngineName,
            ValidationEngineKinds.QualityRule,
            $"Quality rule engine '{EngineName}'.");

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

public sealed record BatchRuleViolation(
    string TargetPath,
    string? Message = null);

public sealed record ResolvedTargetItem(
    string TargetPath,
    JsonNode EnrichedContextNode,
    JsonNode? ChildFocusNode = null,
    IReadOnlyList<JsonNode>? Ancestors = null,
    int? Index = null,
    IReadOnlyDictionary<string, JsonNode?>? AmbientContext = null);

/// <summary>
/// Optional extension interface for engines that evaluate across an entire collection of resolved
/// targets in a single O(N) pass (e.g. composite uniqueness checks).
/// </summary>
public interface IBatchQualityRuleEngine : IQualityRuleEngine
{
    Result<IReadOnlyList<BatchRuleViolation>> EvaluateBatch(
        BusinessRule rule,
        IReadOnlyList<ResolvedTargetItem> targets,
        JsonNode rootNode);
}
