using System.Globalization;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissions;
using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SubmissionsViewModel(
    AdminPageMeta Page,
    GetSubmissionsResult? Result,
    Error? PageError = null)
{
    public ErrorSummaryViewModel? ErrorSummary
    {
        get { return ErrorSummaryViewModel.From(PageError); }
    }

    public string SelectedSchemaName
    {
        get { return Result?.SchemaName ?? string.Empty; }
    }

    public string SelectedVersionDisplay
    {
        get { return Result?.SchemaVersion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty; }
    }

    public string SelectedStatus
    {
        get { return Result?.Status ?? "All"; }
    }

    public string SearchQuery
    {
        get { return Result?.Query ?? string.Empty; }
    }

    public bool HasActiveFilters
    {
        get
        {
            return !string.IsNullOrWhiteSpace(SelectedSchemaName) ||
                   Result?.SchemaVersion is not null ||
                   (!string.IsNullOrWhiteSpace(SelectedStatus) &&
                    !string.Equals(SelectedStatus, "All", StringComparison.OrdinalIgnoreCase)) ||
                   !string.IsNullOrWhiteSpace(SearchQuery);
        }
    }

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
                new("All", "All states",
                    string.Equals(SelectedStatus, "All", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(SelectedStatus)),
                new("Draft", "Draft (all in-progress)",
                    string.Equals(SelectedStatus, "Draft", StringComparison.OrdinalIgnoreCase)),
                new("DraftWithPayload", "Draft (payload saved)",
                    string.Equals(SelectedStatus, "DraftWithPayload", StringComparison.OrdinalIgnoreCase)),
                new("AwaitingPayload", "Draft (awaiting payload)",
                    string.Equals(SelectedStatus, "AwaitingPayload", StringComparison.OrdinalIgnoreCase)),
                new("Finalised", "Finalised (ReadyForCde)",
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

    public bool HasItems
    {
        get { return Items.Count > 0; }
    }

    public int TotalCount
    {
        get { return Items.Count; }
    }

    public int DraftCount
    {
        get { return Result?.Submissions.Count(s => !s.IsFinalised) ?? 0; }
    }

    public int FinalisedCount
    {
        get { return Result?.Submissions.Count(s => s.IsFinalised) ?? 0; }
    }
}
