using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record ErrorSummaryViewModel(
    string Code,
    string Description,
    IReadOnlyList<ValidationErrorItemViewModel> ValidationErrors)
{
    public bool HasValidationErrors => ValidationErrors.Count > 0;

    public static ErrorSummaryViewModel? From(Error? error)
    {
        if (error is null)
        {
            return null;
        }

        var items = (error.ValidationErrors ?? [])
            .Select(ve => new ValidationErrorItemViewModel(
                HasPropertyPath: !string.IsNullOrWhiteSpace(ve.PropertyPath),
                PropertyPath: ve.PropertyPath,
                Message: ve.Message))
            .ToList();

        return new ErrorSummaryViewModel(error.Code, error.Description, items);
    }
}
