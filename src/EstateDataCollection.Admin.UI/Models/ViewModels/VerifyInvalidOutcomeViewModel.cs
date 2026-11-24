using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record VerifyInvalidOutcomeViewModel(
    string Title,
    bool HasDescription,
    string? Description,
    string StatusTagText,
    string StatusTagCssClass,
    IReadOnlyList<ValidationMessage> TriggeredErrors);
