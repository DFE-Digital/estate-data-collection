using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record WorkbenchVerifyViewModel(
    bool HasRun,
    VerifySchemaResult? Result = null,
    Error? Error = null)
{
    public bool ShowPanel
    {
        get { return HasRun; }
    }

    public bool HasError
    {
        get { return HasRun && Error is not null; }
    }

    public ErrorSummaryViewModel? ErrorSummary
    {
        get { return ErrorSummaryViewModel.From(Error); }
    }

    public bool HasResult
    {
        get { return HasRun && Error is null && Result is not null; }
    }

    public NotificationBannerViewModel? SuccessBanner
    {
        get
        {
            return Result is null
                ? null
                : NotificationBannerViewModel.Success(
                    message: Result.Message,
                    title: "Verification passed",
                    detail:
                    "Contract syntax, identifier alignment, quality rules, and any provided sample fixtures passed dry-run verification.");
        }
    }

    public bool HasWarnings
    {
        get { return Result?.Warnings is { Count: > 0 }; }
    }

    public int WarningCount
    {
        get { return Result?.Warnings?.Count ?? 0; }
    }

    public bool HasInvalidFixtureOutcomes
    {
        get { return Result?.InvalidFixtureOutcomes is { Count: > 0 }; }
    }

    public IReadOnlyList<VerifyInvalidOutcomeViewModel> InvalidFixtureOutcomes
    {
        get { return _invalidFixtureOutcomes ??= BuildInvalidFixtureOutcomes(); }
    }

    private IReadOnlyList<VerifyInvalidOutcomeViewModel>? _invalidFixtureOutcomes;

    public bool HasResolvedIdentifierConfig
    {
        get { return Result?.ResolvedIdentifierConfig is not null; }
    }

    public bool IsIdentifierDisabled
    {
        get { return Result?.ResolvedIdentifierConfig?.IsDisabled == true; }
    }

    public bool IsIdentifierEnabled
    {
        get { return Result?.ResolvedIdentifierConfig is { IsDisabled: false }; }
    }

    public string IdentifierType
    {
        get { return Result?.ResolvedIdentifierConfig?.Type ?? string.Empty; }
    }

    public string IdentifierPointerDisplay
    {
        get { return Result?.ResolvedIdentifierConfig?.JsonPointer ?? "default"; }
    }

    public string IdentifierRequiredDisplay
    {
        get { return Result?.ResolvedIdentifierConfig?.Required == true ? "Yes" : "No"; }
    }

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
