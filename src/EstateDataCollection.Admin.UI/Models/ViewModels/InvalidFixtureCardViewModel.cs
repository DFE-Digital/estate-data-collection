using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record InvalidFixtureCardViewModel(
    string Title,
    bool HasDescription,
    string? Description,
    string StatusTagText,
    string StatusTagCssClass,
    bool ShowSoftWarningCallout,
    bool HasExpectedAssertions,
    IReadOnlyList<ExpectedErrorAssertionViewModel> ExpectedAssertions,
    bool HasOutcome,
    int TriggeredErrorCount,
    bool PassedUnexpectedly,
    bool HasTriggeredErrors,
    IReadOnlyList<ValidationMessage> TriggeredErrors,
    string FormattedPayloadJson);
