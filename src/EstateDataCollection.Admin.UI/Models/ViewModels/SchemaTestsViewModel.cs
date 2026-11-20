using System.Text.Json;
using System.Text.Json.Nodes;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Domain.Schema;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaTestsViewModel(
    AdminPageMeta Page,
    string SchemaName,
    int Version,
    SchemaVersionDetail? Detail,
    RunSchemaTestsResult? LastRunResult = null,
    string? SuccessBanner = null,
    Error? PageError = null,
    string? ValidFixtureInput = null,
    string? InvalidFixtureInput = null)
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private SchemaTestSuiteItemResult? LatestItem
    {
        get { return LastRunResult?.Results.FirstOrDefault(); }
    }

    private IReadOnlyList<string> ValidFixtureRawList
    {
        get { return Detail?.ValidFixtures ?? []; }
    }

    private IReadOnlyList<string> InvalidFixtureRawList
    {
        get { return Detail?.InvalidFixtures ?? []; }
    }

    private IReadOnlyList<string> SoftWarnings
    {
        get { return _softWarnings ??= ResolveSoftWarnings(); }
    }

    private IReadOnlyList<string>? _softWarnings;

    public NotificationBannerViewModel? SuccessBannerModel
    {
        get { return NotificationBannerViewModel.Success(SuccessBanner); }
    }

    public ErrorSummaryViewModel? ErrorSummary
    {
        get { return ErrorSummaryViewModel.From(PageError); }
    }

    public string BackToDetailsUrl
    {
        get { return AppRoutes.AdminUi.SchemaDetail(SchemaName, Version); }
    }

    public bool HasDetail
    {
        get { return Detail is not null; }
    }

    public string DetailSchemaName
    {
        get { return Detail?.SchemaName ?? SchemaName; }
    }

    public string DetailVersionDisplay
    {
        get { return $"v{Detail?.Version ?? Version}"; }
    }

    public string RunTestsActionUrl
    {
        get
        {
            return Detail is null
                ? string.Empty
                : AppRoutes.AdminUi.SchemaTestsRun(Detail.SchemaName, Detail.Version);
        }
    }

    public string UpdateTestsActionUrl
    {
        get
        {
            return Detail is null
                ? string.Empty
                : AppRoutes.AdminUi.SchemaTests(Detail.SchemaName, Detail.Version);
        }
    }

    public LifecycleTagViewModel? StateTag
    {
        get { return Detail is null ? null : LifecycleTagViewModel.From(Detail.State.Name); }
    }

    public int ValidFixturesCount
    {
        get { return ValidFixtureRawList.Count; }
    }

    public int InvalidFixturesCount
    {
        get { return InvalidFixtureRawList.Count; }
    }

    public int BusinessRuleCount
    {
        get { return Detail?.QualityRules.Count ?? 0; }
    }

    public bool HasBusinessRules
    {
        get { return BusinessRuleCount > 0; }
    }

    public string BusinessRuleEnginesSummary
    {
        get
        {
            return Detail is { QualityRules.Count: > 0 }
                ? $"({string.Join(", ", Detail.QualityRules.Select(r => r.Engine).Distinct(StringComparer.OrdinalIgnoreCase))})"
                : string.Empty;
        }
    }

    public QualityRulesViewerViewModel QualityRulesViewer
    {
        get { return QualityRulesViewerViewModel.From(Detail?.QualityRules); }
    }

    public bool ShowLatestRunPassedBanner
    {
        get { return LatestItem is { Passed: true }; }
    }

    public bool ShowLatestRunFailedBanner
    {
        get { return LatestItem is { Passed: false }; }
    }

    public string LatestRunAppVersion
    {
        get { return LastRunResult?.AppVersion ?? string.Empty; }
    }

    public int LatestRunErrorCount
    {
        get { return LatestItem?.Errors.Count ?? 0; }
    }

    public IReadOnlyList<string> LatestRunErrors
    {
        get { return LatestItem?.Errors ?? []; }
    }

    public string LatestRunEvaluationSummary
    {
        get
        {
            return LatestItem is null
                ? string.Empty
                : $"Evaluated {LatestItem.ValidFixtureCount} valid fixture(s), {LatestItem.InvalidFixtureCount} invalid fixture(s), and {LatestItem.BusinessRuleCount} business rule(s).";
        }
    }

    public int SoftWarningCount
    {
        get { return SoftWarnings.Count; }
    }

    public bool ShowSoftWarningBanner
    {
        get { return SoftWarnings.Count > 0 && (LatestItem?.Passed ?? Detail?.LastTestRunPassed) == true; }
    }

    public bool HasLastRecordedRun
    {
        get { return Detail?.LastTestRunAtUtc.HasValue == true; }
    }

    public bool NoLastRecordedRun
    {
        get { return !HasLastRecordedRun; }
    }

    public bool LastRecordedRunPassed
    {
        get { return Detail?.LastTestRunPassed == true; }
    }

    public bool LastRecordedRunFailed
    {
        get { return HasLastRecordedRun && Detail?.LastTestRunPassed != true; }
    }

    public bool ShowLastRunSoftWarningTag
    {
        get { return LastRecordedRunPassed && SoftWarnings.Count > 0; }
    }

    public string LastRunTimestampText
    {
        get { return Detail?.LastTestRunAtUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? string.Empty; }
    }

    public bool HasLastRunAppVersion
    {
        get { return !string.IsNullOrWhiteSpace(Detail?.LastTestRunAppVersion); }
    }

    public string LastRunAppVersion
    {
        get { return Detail?.LastTestRunAppVersion ?? string.Empty; }
    }

    public FixtureEditorFieldViewModel ValidFixtureField
    {
        get
        {
            return _validFixtureField ??= BuildFixtureEditorField(
                formFieldName: "validFixtureJson",
                verificationPrefix: "SampleValidFixtures",
                inputOverride: ValidFixtureInput,
                storedFixtures: ValidFixtureRawList);
        }
    }

    private FixtureEditorFieldViewModel? _validFixtureField;

    public FixtureEditorFieldViewModel InvalidFixtureField
    {
        get
        {
            return _invalidFixtureField ??= BuildFixtureEditorField(
                formFieldName: "invalidFixtureJson",
                verificationPrefix: "SampleInvalidFixtures",
                inputOverride: InvalidFixtureInput,
                storedFixtures: InvalidFixtureRawList);
        }
    }

    private FixtureEditorFieldViewModel? _invalidFixtureField;

    public bool HasValidFixtures
    {
        get { return ValidFixturesCount > 0; }
    }

    public bool NoValidFixtures
    {
        get { return ValidFixturesCount == 0; }
    }

    public IReadOnlyList<ValidFixtureCardViewModel> ValidFixtures
    {
        get
        {
            return _validFixtures ??= ValidFixtureRawList
                .Select((raw, index) => new ValidFixtureCardViewModel(
                    Title: $"Valid Fixture #{index + 1}",
                    FormattedJson: FormatJson(raw)))
                .ToList();
        }
    }

    private IReadOnlyList<ValidFixtureCardViewModel>? _validFixtures;

    public bool HasInvalidFixtures
    {
        get { return InvalidFixturesCount > 0; }
    }

    public bool NoInvalidFixtures
    {
        get { return InvalidFixturesCount == 0; }
    }

    public IReadOnlyList<InvalidFixtureCardViewModel> InvalidFixtures
    {
        get { return _invalidFixtures ??= BuildInvalidFixtureCards(); }
    }

    private IReadOnlyList<InvalidFixtureCardViewModel>? _invalidFixtures;

    private IReadOnlyList<string> ResolveSoftWarnings()
    {
        if (Detail is null)
        {
            return [];
        }

        var invalidOutcomes = LatestItem?.InvalidFixtureOutcomes ?? Detail.LastTestRunInvalidFixtures ?? [];
        var softWarnings = (LatestItem?.Warnings ?? Detail.LastTestRunWarnings ?? []).ToList();
        if (softWarnings.Count == 0 && invalidOutcomes.Count > 0)
        {
            softWarnings = invalidOutcomes
                .Where(o => !string.IsNullOrWhiteSpace(o.Warning))
                .Select(o => $"SampleInvalidFixtures:{o.Index} {o.Warning}")
                .ToList();
        }

        return softWarnings;
    }

    private FixtureEditorFieldViewModel BuildFixtureEditorField(
        string formFieldName,
        string verificationPrefix,
        string? inputOverride,
        IReadOnlyList<string> storedFixtures)
    {
        var fieldError = GetFieldError(formFieldName, verificationPrefix);
        var hasError = fieldError is not null;

        return new FixtureEditorFieldViewModel(
            EditorText: inputOverride ?? FormatFixturesForEditor(storedFixtures),
            HasError: hasError,
            ErrorMessage: fieldError,
            FormGroupCssClass: hasError ? "govuk-form-group govuk-form-group--error" : "govuk-form-group",
            TextareaCssClass: hasError
                ? "govuk-textarea dfe-code-textarea govuk-textarea--error"
                : "govuk-textarea dfe-code-textarea",
            AriaDescribedBy: hasError
                ? $"{formFieldName}-hint {formFieldName}-error"
                : $"{formFieldName}-hint");
    }

    private IReadOnlyList<InvalidFixtureCardViewModel> BuildInvalidFixtureCards()
    {
        if (Detail is null || InvalidFixtureRawList.Count == 0)
        {
            return [];
        }

        var invalidOutcomes = LatestItem?.InvalidFixtureOutcomes ?? Detail.LastTestRunInvalidFixtures ?? [];
        var cards = new List<InvalidFixtureCardViewModel>(InvalidFixtureRawList.Count);

        for (var i = 0; i < InvalidFixtureRawList.Count; i++)
        {
            var raw = InvalidFixtureRawList[i];
            var spec = InvalidFixtureParser.Parse(raw);
            var outcome = invalidOutcomes.FirstOrDefault(o => o.Index == i);
            var expectedList = outcome?.ExpectedErrors is { Count: > 0 }
                ? outcome.ExpectedErrors
                : spec.ExpectedErrors;
            var unmatchedSet = outcome?.UnmatchedExpectedErrors ?? [];
            var displayPayload = spec.HasWrapper ? spec.PayloadJson : raw;
            var description = outcome?.Description ?? spec.Description;
            var hasDescription = !string.IsNullOrWhiteSpace(description);

            string statusTagText;
            string statusTagCssClass;
            if (outcome is not null)
            {
                if (!outcome.FailedAsExpected)
                {
                    statusTagText = outcome.TriggeredErrors.Count == 0
                        ? "Passed unexpectedly"
                        : "Expectation mismatch";
                    statusTagCssClass = "govuk-tag govuk-tag--red";
                }
                else if (expectedList.Count > 0)
                {
                    statusTagText = "Matched expected error(s)";
                    statusTagCssClass = "govuk-tag govuk-tag--green";
                }
                else
                {
                    statusTagText = "Failed \u2014 unknown if expected";
                    statusTagCssClass = "govuk-tag govuk-tag--yellow";
                }
            }
            else
            {
                statusTagText = "Pending evaluation";
                statusTagCssClass = "govuk-tag govuk-tag--grey";
            }

            var assertions = expectedList
                .Select(expectedErr =>
                {
                    string tagText;
                    string tagCssClass;
                    if (outcome is null)
                    {
                        tagText = "Configured";
                        tagCssClass = "govuk-tag govuk-tag--grey govuk-!-margin-right-1";
                    }
                    else if (unmatchedSet.Any(u => string.Equals(u, expectedErr, StringComparison.OrdinalIgnoreCase)))
                    {
                        tagText = "Missing";
                        tagCssClass = "govuk-tag govuk-tag--red govuk-!-margin-right-1";
                    }
                    else
                    {
                        tagText = "Matched";
                        tagCssClass = "govuk-tag govuk-tag--green govuk-!-margin-right-1";
                    }

                    return new ExpectedErrorAssertionViewModel(expectedErr, tagText, tagCssClass);
                })
                .ToList();

            var showSoftWarningCallout = outcome is not null
                && outcome.FailedAsExpected
                && expectedList.Count == 0
                && outcome.TriggeredErrors.Count > 0;

            var triggeredErrors = outcome?.TriggeredErrors ?? [];

            cards.Add(new InvalidFixtureCardViewModel(
                Title: $"Invalid Fixture #{i + 1}",
                HasDescription: hasDescription,
                Description: hasDescription ? description : null,
                StatusTagText: statusTagText,
                StatusTagCssClass: statusTagCssClass,
                ShowSoftWarningCallout: showSoftWarningCallout,
                HasExpectedAssertions: assertions.Count > 0,
                ExpectedAssertions: assertions,
                HasOutcome: outcome is not null,
                TriggeredErrorCount: triggeredErrors.Count,
                PassedUnexpectedly: outcome is not null && triggeredErrors.Count == 0,
                HasTriggeredErrors: outcome is not null && triggeredErrors.Count > 0,
                TriggeredErrors: triggeredErrors,
                FormattedPayloadJson: FormatJson(displayPayload)));
        }

        return cards;
    }

    private string? GetFieldError(string formFieldName, string verificationPrefix)
    {
        var errors = PageError?.ValidationErrors;
        if (errors is null || errors.Count == 0)
        {
            return null;
        }

        var matches = errors
            .Where(e =>
                string.Equals(e.PropertyPath, formFieldName, StringComparison.OrdinalIgnoreCase) ||
                e.PropertyPath.StartsWith(verificationPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Message)
            .ToList();

        return matches.Count > 0 ? string.Join(" ", matches) : null;
    }

    private static string FormatFixturesForEditor(IReadOnlyList<string> fixtures)
    {
        if (fixtures.Count == 0)
        {
            return string.Empty;
        }

        var array = new JsonArray();
        foreach (var raw in fixtures)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            try
            {
                var parsed = JsonNode.Parse(raw);
                if (parsed is not null)
                {
                    array.Add(parsed);
                    continue;
                }
            }
            catch
            {
                // Preserve raw value if malformed
            }

            array.Add(JsonValue.Create(raw));
        }

        return array.ToJsonString(IndentedJsonOptions);
    }

    private static string FormatJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, IndentedJsonOptions);
        }
        catch
        {
            return raw;
        }
    }
}
