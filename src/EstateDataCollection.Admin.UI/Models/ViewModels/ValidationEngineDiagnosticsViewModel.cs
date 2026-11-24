using System.Globalization;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record ValidationEngineDiagnosticsViewModel(
    string Heading,
    string? Subtitle,
    IReadOnlyList<ValidationEngineTraceRowViewModel> TraceRows,
    string RegisteredHeading,
    IReadOnlyList<RegisteredValidationEngineRowViewModel> RegisteredEngines,
    string? RegisteredSubtitle = null)
{
    public static readonly ValidationEngineDiagnosticsViewModel Empty = new(
        Heading: "Validation Engine Diagnostics",
        Subtitle: null,
        TraceRows: [],
        RegisteredHeading: "Registered Validation Engines",
        RegisteredEngines: [],
        RegisteredSubtitle: null);

    public bool HasTraces
    {
        get { return TraceRows.Count > 0; }
    }

    public bool HasSubtitle
    {
        get { return !string.IsNullOrWhiteSpace(Subtitle); }
    }

    public bool HasRegisteredEngines
    {
        get { return RegisteredEngines.Count > 0; }
    }

    public bool HasRegisteredSubtitle
    {
        get { return !string.IsNullOrWhiteSpace(RegisteredSubtitle); }
    }

    public bool HasContent
    {
        get { return HasTraces || HasRegisteredEngines; }
    }

    public static ValidationEngineDiagnosticsViewModel FromTraces(
        IReadOnlyList<ValidationEngineTrace>? traces,
        string heading = "Validation Engine Diagnostics",
        string? subtitle = null)
    {
        var rows = MapTraceRows(traces);
        var resolvedSubtitle = subtitle ?? (rows.Count > 0
            ? $"Execution outcomes, evaluated items, violations, and timings recorded across {rows.Count} validation engine(s)."
            : null);

        return new ValidationEngineDiagnosticsViewModel(
            Heading: heading,
            Subtitle: resolvedSubtitle,
            TraceRows: rows,
            RegisteredHeading: "Registered Validation Engines",
            RegisteredEngines: [],
            RegisteredSubtitle: null);
    }

    public static ValidationEngineDiagnosticsViewModel FromDetail(
        IReadOnlyList<ValidationEngineTrace>? traces,
        IReadOnlyList<ValidationEngineDescriptor>? registeredEngines,
        IReadOnlyList<SchemaQualityRuleSummary>? qualityRules,
        SchemaIdentifierConfig? identifierConfig,
        string traceHeading = "Last Test Run Engine Diagnostics",
        string? traceSubtitle = null,
        string registeredHeading = "Registered Validation Engines",
        string? registeredSubtitle = null)
    {
        var traceRows = MapTraceRows(traces);
        var registeredRows = MapRegisteredEngines(registeredEngines, qualityRules, identifierConfig);
        var resolvedTraceSubtitle = traceSubtitle ?? (traceRows.Count > 0
            ? $"Execution outcomes, evaluated items, violations, and timings recorded across {traceRows.Count} validation engine(s) in the latest regression test run."
            : null);
        var applicableCount = registeredRows.Count(r =>
            string.Equals(r.ApplicabilityTagCssClass, "govuk-tag govuk-tag--green", StringComparison.Ordinal));
        var resolvedRegisteredSubtitle = registeredSubtitle ?? (registeredRows.Count > 0
            ? $"All validation engines currently registered in this environment and whether they apply to this schema version ({applicableCount} of {registeredRows.Count} active or applicable)."
            : null);

        return new ValidationEngineDiagnosticsViewModel(
            Heading: traceHeading,
            Subtitle: resolvedTraceSubtitle,
            TraceRows: traceRows,
            RegisteredHeading: registeredHeading,
            RegisteredEngines: registeredRows,
            RegisteredSubtitle: resolvedRegisteredSubtitle);
    }

    public static string? FormatCompactSummary(IReadOnlyList<ValidationEngineTrace>? traces)
    {
        if (traces is not { Count: > 0 })
        {
            return null;
        }

        var active = traces
            .Where(t => !string.Equals(t.Outcome, nameof(SubmissionProcessOutcome.Skipped), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var selected = active.Count > 0 ? active : traces;

        return string.Join(", ", selected.Select(t => $"{t.Descriptor.EngineId}: {t.Outcome}"));
    }

    private static IReadOnlyList<ValidationEngineTraceRowViewModel> MapTraceRows(
        IReadOnlyList<ValidationEngineTrace>? traces)
    {
        if (traces is not { Count: > 0 })
        {
            return [];
        }

        return traces
            .Select(t => new ValidationEngineTraceRowViewModel(
                EngineId: t.Descriptor.EngineId,
                DisplayName: t.Descriptor.DisplayName,
                KindLabel: FormatKindLabel(t.Descriptor.Kind),
                Description: t.Descriptor.Description,
                OutcomeText: t.Outcome,
                OutcomeTagCssClass: MapOutcomeTagCss(t.Outcome),
                ItemsEvaluatedDisplay: t.ItemsEvaluated.ToString(CultureInfo.InvariantCulture),
                ViolationCountDisplay: t.ViolationCount.ToString(CultureInfo.InvariantCulture),
                DurationDisplay: $"{t.DurationMs} ms",
                SummaryDisplay: string.IsNullOrWhiteSpace(t.Summary) ? "—" : t.Summary))
            .ToList();
    }

    private static IReadOnlyList<RegisteredValidationEngineRowViewModel> MapRegisteredEngines(
        IReadOnlyList<ValidationEngineDescriptor>? registeredEngines,
        IReadOnlyList<SchemaQualityRuleSummary>? qualityRules,
        SchemaIdentifierConfig? identifierConfig)
    {
        if (registeredEngines is not { Count: > 0 })
        {
            return [];
        }

        var rules = qualityRules ?? [];

        return registeredEngines
            .Select(d =>
            {
                string appText;
                string appCss;

                if (string.Equals(d.Kind, ValidationEngineKinds.StructuralSchema, StringComparison.OrdinalIgnoreCase))
                {
                    appText = "Applicable";
                    appCss = "govuk-tag govuk-tag--green";
                }
                else if (string.Equals(d.Kind, ValidationEngineKinds.IdentifierExtraction, StringComparison.OrdinalIgnoreCase))
                {
                    if (identifierConfig?.IsDisabled == true)
                    {
                        appText = "Disabled";
                        appCss = "govuk-tag govuk-tag--grey";
                    }
                    else
                    {
                        appText = "Applicable";
                        appCss = "govuk-tag govuk-tag--green";
                    }
                }
                else
                {
                    var matchingCount = rules.Count(r =>
                        string.Equals(r.Engine, d.EngineId, StringComparison.OrdinalIgnoreCase));
                    if (matchingCount > 0)
                    {
                        appText = $"Active ({matchingCount} rule{(matchingCount == 1 ? string.Empty : "s")})";
                        appCss = "govuk-tag govuk-tag--green";
                    }
                    else
                    {
                        appText = "Idle (0 rules)";
                        appCss = "govuk-tag govuk-tag--grey";
                    }
                }

                return new RegisteredValidationEngineRowViewModel(
                    EngineId: d.EngineId,
                    DisplayName: d.DisplayName,
                    KindLabel: FormatKindLabel(d.Kind),
                    Description: d.Description,
                    ApplicabilityText: appText,
                    ApplicabilityTagCssClass: appCss);
            })
            .ToList();
    }

    private static string FormatKindLabel(string kind)
    {
        return kind switch
        {
            ValidationEngineKinds.StructuralSchema => "Structural Schema",
            ValidationEngineKinds.QualityRule => "Quality Rule",
            ValidationEngineKinds.IdentifierExtraction => "Identifier Extraction",
            _ => string.IsNullOrWhiteSpace(kind) ? "Unknown" : kind
        };
    }

    private static string MapOutcomeTagCss(string outcome)
    {
        return outcome switch
        {
            nameof(SubmissionProcessOutcome.Succeeded) => "govuk-tag govuk-tag--green",
            nameof(SubmissionProcessOutcome.ValidationErrorsFound) => "govuk-tag govuk-tag--yellow",
            nameof(SubmissionProcessOutcome.Failed) => "govuk-tag govuk-tag--red",
            nameof(SubmissionProcessOutcome.Skipped)
                or nameof(SubmissionProcessOutcome.Missing) => "govuk-tag govuk-tag--grey",
            _ => "govuk-tag govuk-tag--grey"
        };
    }
}
