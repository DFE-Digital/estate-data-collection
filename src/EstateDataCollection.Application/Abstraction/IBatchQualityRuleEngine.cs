using System.Text.Json.Nodes;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Application.Abstraction;

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