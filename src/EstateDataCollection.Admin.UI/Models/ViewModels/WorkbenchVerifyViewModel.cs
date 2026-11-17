using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record WorkbenchVerifyViewModel(
    bool HasRun,
    VerifySchemaResult? Result = null,
    Error? Error = null)
{
    public bool ShowPanel => HasRun;

    public bool HasError => HasRun && Error is not null;

    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(Error);

    public bool HasResult => HasRun && Error is null && Result is not null;

    public NotificationBannerViewModel? SuccessBanner =>
        Result is null
            ? null
            : NotificationBannerViewModel.Success(
                message: Result.Message,
                title: "Verification passed",
                detail: "Contract syntax, identifier alignment, quality rules, and any provided sample fixtures passed dry-run verification.");

    public bool HasWarnings => Result?.Warnings is { Count: > 0 };

    public int WarningCount => Result?.Warnings?.Count ?? 0;

    public bool HasInvalidFixtureOutcomes => Result?.InvalidFixtureOutcomes is { Count: > 0 };

    public IReadOnlyList<VerifyInvalidOutcomeViewModel> InvalidFixtureOutcomes =>
        _invalidFixtureOutcomes ??= BuildInvalidFixtureOutcomes();

    private IReadOnlyList<VerifyInvalidOutcomeViewModel>? _invalidFixtureOutcomes;

    public bool HasResolvedIdentifierConfig => Result?.ResolvedIdentifierConfig is not null;

    public bool IsIdentifierDisabled => Result?.ResolvedIdentifierConfig?.IsDisabled == true;

    public bool IsIdentifierEnabled => Result?.ResolvedIdentifierConfig is { IsDisabled: false };

    public string IdentifierType => Result?.ResolvedIdentifierConfig?.Type ?? string.Empty;

    public string IdentifierPointerDisplay => Result?.ResolvedIdentifierConfig?.JsonPointer ?? "default";

    public string IdentifierRequiredDisplay =>
        Result?.ResolvedIdentifierConfig?.Required == true ? "Yes" : "No";

    private IReadOnlyList<VerifyInvalidOutcomeViewModel> BuildInvalidFixtureOutcomes()
    {
        var outcomes = Result?.InvalidFixtureOutcomes;
        if (outcomes is null || outcomes.Count == 0)
        {
            return [];
        }

        return outcomes
            .Select(o =>
            {
                var hasExpected = o.ExpectedErrors is { Count: > 0 };
                return new VerifyInvalidOutcomeViewModel(
                    Title: $"Invalid Fixture #{o.Index + 1}",
                    HasDescription: !string.IsNullOrWhiteSpace(o.Description),
                    Description: o.Description,
                    StatusTagText: hasExpected
                        ? "Matched expected error(s)"
                        : "Failed \u2014 unknown if expected",
                    StatusTagCssClass: hasExpected
                        ? "govuk-tag govuk-tag--green"
                        : "govuk-tag govuk-tag--yellow",
                    TriggeredErrors: o.TriggeredErrors);
            })
            .ToList();
    }
}
