using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Application.Models.Contracts;

public sealed record SchemaVerificationResult(
    IReadOnlyList<ValidationMessage> SyntaxErrors,
    IReadOnlyList<ValidationMessage> FixtureErrors,
    SchemaIdentifierConfig? ResolvedIdentifierConfig = null,
    IReadOnlyList<InvalidFixtureOutcome>? InvalidFixtureOutcomes = null,
    IReadOnlyList<ValidationMessage>? Warnings = null,
    IReadOnlyList<ValidationEngineTrace>? EngineTraces = null,
    IReadOnlyList<ValidationError>? AnalyzerFindings = null,
    IReadOnlyList<ValidationError>? AdditionalIssues = null
)
{
    public bool IsSuccessful => SyntaxErrors.Count == 0 && FixtureErrors.Count == 0;

    public bool HasWarnings
    {
        get
        {
            return Warnings is { Count: > 0 } ||
                   (AnalyzerFindings?.Any(f => f.Severity == RuleSeverity.Warning) == true);
        }
    }

    public IReadOnlyList<string> Errors
    {
        get { return [.. SyntaxErrors.Select(e => e.Message), .. FixtureErrors.Select(e => e.Message)]; }
    }

    public IReadOnlyList<string> WarningMessages
    {
        get { return [.. (Warnings ?? []).Select(w => w.Message)]; }
    }

    public IReadOnlyList<ValidationMessage> ValidationMessages => [.. SyntaxErrors, .. FixtureErrors];

    public IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>> Issues => BuildIssues();

    public IReadOnlyList<ValidationError> ToValidationErrors(string defaultPropertyPath = "schema")
    {
        return
        [
            .. SyntaxErrors.Select(e => new ValidationError(
                string.IsNullOrWhiteSpace(e.Path)
                    ? (string.IsNullOrWhiteSpace(e.Identifier) ? defaultPropertyPath : e.Identifier)
                    : e.Path,
                e.Message,
                e.RuleId,
                RuleSeverity.Error,
                EngineId: e.EngineType?.Name)),
            .. FixtureErrors.Select(e => new ValidationError(
                string.IsNullOrWhiteSpace(e.Path)
                    ? (string.IsNullOrWhiteSpace(e.Identifier) ? "fixtures" : e.Identifier)
                    : e.Path,
                e.Message,
                e.RuleId,
                RuleSeverity.Error,
                EngineId: e.EngineType?.Name))
        ];
    }

    public SchemaErrorReport ToErrorReport(string schemaName, int version)
    {
        var summary = !IsSuccessful
            ? string.Join("; ", Errors.Take(3))
            : WarningMessages.Count > 0
                ? string.Join("; ", WarningMessages.Take(3))
                : null;

        return new SchemaErrorReport(
            SchemaName: schemaName,
            Version: version,
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Issues: Issues,
            SyntaxErrors: SyntaxErrors.Select(e => e.Message).ToArray(),
            FixtureErrors: FixtureErrors.Select(e => e.Message).ToArray(),
            Warnings: WarningMessages,
            Summary: summary);
    }

    private IReadOnlyDictionary<RuleSeverity, IReadOnlyList<ValidationError>> BuildIssues()
    {
        var list = new List<ValidationError>();
        if (AnalyzerFindings is { Count: > 0 })
        {
            list.AddRange(AnalyzerFindings);
        }

        if (AdditionalIssues is { Count: > 0 })
        {
            list.AddRange(AdditionalIssues);
        }

        foreach (var err in SyntaxErrors)
        {
            if (string.Equals(err.Source, "Consistency", StringComparison.OrdinalIgnoreCase) ||
                err.Metadata?.ContainsKey("capturedInIssues") == true)
            {
                continue;
            }

            list.Add(new ValidationError(
                PropertyPath: string.IsNullOrWhiteSpace(err.Path)
                    ? (string.IsNullOrWhiteSpace(err.Identifier) ? "schema" : err.Identifier)
                    : err.Path,
                Message: err.Message,
                RuleId: err.RuleId,
                Severity: RuleSeverity.Error,
                Action: "reject_schema",
                EngineId: err.EngineType?.Name));
        }

        foreach (var err in FixtureErrors)
        {
            if (err.Metadata?.ContainsKey("capturedInIssues") == true)
            {
                continue;
            }

            list.Add(new ValidationError(
                PropertyPath: string.IsNullOrWhiteSpace(err.Path)
                    ? (string.IsNullOrWhiteSpace(err.Identifier) ? "fixtures" : err.Identifier)
                    : err.Path,
                Message: err.Message,
                RuleId: err.RuleId,
                Severity: RuleSeverity.Error,
                Action: "reject_schema",
                EngineId: err.EngineType?.Name));
        }

        foreach (var warn in Warnings ?? [])
        {
            if (string.Equals(warn.Source, "Consistency", StringComparison.OrdinalIgnoreCase) ||
                warn.Metadata?.ContainsKey("capturedInIssues") == true)
            {
                continue;
            }

            list.Add(new ValidationError(
                PropertyPath: string.IsNullOrWhiteSpace(warn.Path)
                    ? (string.IsNullOrWhiteSpace(warn.Identifier) ? "fixtures" : warn.Identifier)
                    : warn.Path,
                Message: warn.Message,
                RuleId: warn.RuleId,
                Severity: RuleSeverity.Warning,
                Action: "highlight_issue",
                EngineId: warn.EngineType?.Name));
        }

        return ValidationIssuesHelper.GroupBySeverity(list);
    }
}
