using System.Globalization;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissions;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SubmissionsViewModel(
    AdminPageMeta Page,
    GetSubmissionsResult? Result,
    Error? PageError = null)
{
    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(PageError);

    public string SelectedSchemaName => Result?.SchemaName ?? string.Empty;

    public string SelectedVersionDisplay => Result?.SchemaVersion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public string SelectedStatus => Result?.Status ?? "All";

    public string SearchQuery => Result?.Query ?? string.Empty;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SelectedSchemaName) ||
        Result?.SchemaVersion is not null ||
        (!string.IsNullOrWhiteSpace(SelectedStatus) &&
         !string.Equals(SelectedStatus, "All", StringComparison.OrdinalIgnoreCase)) ||
        !string.IsNullOrWhiteSpace(SearchQuery);

    public IReadOnlyList<SelectSchemaFilterOptionViewModel> SchemaOptions
    {
        get
        {
            return _schemaOptions ??= (Result?.AvailableSchemaNames ?? [])
                .Select(name => new SelectSchemaFilterOptionViewModel(
                    Value: name,
                    Label: name,
                    IsSelected: string.Equals(name, SelectedSchemaName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    private IReadOnlyList<SelectSchemaFilterOptionViewModel>? _schemaOptions;

    public IReadOnlyList<SubmissionStatusFilterOptionViewModel> StatusOptions
    {
        get
        {
            return _statusOptions ??=
            [
                new("All", AdminUiMessages.FilterStatusAllStates,
                    string.Equals(SelectedStatus, "All", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(SelectedStatus)),
                new("Draft", AdminUiMessages.FilterStatusDraftAllInProgress,
                    string.Equals(SelectedStatus, "Draft", StringComparison.OrdinalIgnoreCase)),
                new("DraftWithPayload", AdminUiMessages.SubmissionDraftPayloadSavedText,
                    string.Equals(SelectedStatus, "DraftWithPayload", StringComparison.OrdinalIgnoreCase)),
                new("AwaitingPayload", AdminUiMessages.SubmissionDraftAwaitingPayloadText,
                    string.Equals(SelectedStatus, "AwaitingPayload", StringComparison.OrdinalIgnoreCase)),
                new("Finalised", AdminUiMessages.FilterStatusFinalisedReadyForCde,
                    string.Equals(SelectedStatus, "Finalised", StringComparison.OrdinalIgnoreCase))
            ];
        }
    }

    private IReadOnlyList<SubmissionStatusFilterOptionViewModel>? _statusOptions;

    public IReadOnlyList<SubmissionListItemViewModel> Items
    {
        get
        {
            return _items ??= (Result?.Submissions ?? [])
                .Select(SubmissionListItemViewModel.From)
                .ToList();
        }
    }

    private IReadOnlyList<SubmissionListItemViewModel>? _items;

    public bool HasItems => Items.Count > 0;

    public int TotalCount => Items.Count;

    public int DraftCount
    {
        get { return Result?.Submissions.Count(s => !s.IsFinalised) ?? 0; }
    }

    public int FinalisedCount
    {
        get { return Result?.Submissions.Count(s => s.IsFinalised) ?? 0; }
    }
}
