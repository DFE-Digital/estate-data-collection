using EstateDataCollection.Application.Models.Contracts;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record QualityRulesViewerViewModel(
    int RuleCount,
    bool HasRules,
    bool NoRules,
    IReadOnlyList<QualityRuleCardViewModel> Rules)
{
    public static QualityRulesViewerViewModel From(IReadOnlyList<SchemaQualityRuleSummary>? rules)
    {
        var cards = (rules ?? [])
            .Select(r => new QualityRuleCardViewModel(
                Id: r.Id,
                Description: r.Description ?? string.Empty,
                Engine: r.Engine ?? string.Empty,
                Severity: r.Severity ?? string.Empty,
                ErrorMessage: r.ErrorMessage))
            .ToList();

        return new QualityRulesViewerViewModel(
            RuleCount: cards.Count,
            HasRules: cards.Count > 0,
            NoRules: cards.Count == 0,
            Rules: cards);
    }
}
