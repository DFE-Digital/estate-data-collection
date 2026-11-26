using EstateDataCollection.Common.Results;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record WorkbenchViewModel(
    AdminPageMeta Page,
    string InputMode = "odcs",
    string SchemaName = "",
    int Version = 1,
    string FileName = "",
    string OdcsYaml = "",
    string SchemaJson = "",
    string RulesJson = "",
    string ValidFixtureJson = "",
    string InvalidFixtureJson = "",
    string IdentifierMode = "enabled",
    string IdentifierProperty = IdentifierPropertyDiscovery.CustomOptionValue,
    string IdentifierPointer = "",
    string IdentifierLabel = "",
    bool? IdentifierRequired = null,
    IReadOnlyList<SchemaIdentifierPropertyOption>? AvailableIdentifierProperties = null,
    WorkbenchVerifyViewModel? Verification = null,
    Error? PageError = null)
{
    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(PageError);

    public bool IsJsonMode => string.Equals(InputMode, "json", StringComparison.Ordinal);

    public bool IsOdcsMode => !IsJsonMode;

    public bool AreFixturesExpanded =>
        !string.IsNullOrWhiteSpace(ValidFixtureJson) ||
        !string.IsNullOrWhiteSpace(InvalidFixtureJson);

    public IdentifierConfigFieldsetViewModel IdentifierConfig
    {
        get
        {
            return _identifierConfig ??= IdentifierConfigFieldsetViewModel.Create(
                mode: IdentifierMode,
                selectedProperty: IdentifierProperty,
                pointer: IdentifierPointer,
                label: IdentifierLabel,
                required: IdentifierRequired,
                availableProperties: AvailableIdentifierProperties);
        }
    }

    private IdentifierConfigFieldsetViewModel? _identifierConfig;
}
