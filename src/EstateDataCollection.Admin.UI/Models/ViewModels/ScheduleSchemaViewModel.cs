using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Messages;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record ScheduleSchemaViewModel(
    AdminPageMeta Page,
    string SchemaName,
    int Version,
    SchemaVersionDetail? Detail,
    string SelectedAction = "Schedule",
    string EffectiveFromInput = "",
    string EffectiveToInput = "",
    string IdentifierMode = "enabled",
    string IdentifierProperty = IdentifierPropertyDiscovery.CustomOptionValue,
    string IdentifierPointer = "",
    string IdentifierLabel = "",
    bool? IdentifierRequired = null,
    IReadOnlyList<SchemaIdentifierPropertyOption>? AvailableIdentifierProperties = null,
    Error? PageError = null)
{
    public ErrorSummaryViewModel? ErrorSummary => ErrorSummaryViewModel.From(PageError);

    public string BackToDetailsUrl => AppRoutes.AdminUi.SchemaDetail(SchemaName, Version);

    public string BackToDetailsText => AdminUiMessages.BackToSchemaDetails(SchemaName, Version);

    public string HeadingText => AdminUiMessages.ManageSchemaHeading(SchemaName, Version);

    public string FormActionUrl => AppRoutes.AdminUi.SchemaSchedule(SchemaName, Version);

    public bool HasDetail => Detail is not null;

    public LifecycleTagViewModel? DetailStateTag => Detail is null ? null : LifecycleTagViewModel.From(Detail.State.Name);

    public string ShortSha256Hash =>
        Detail is null
            ? string.Empty
            : (Detail.Sha256Hash.Length > 12 ? Detail.Sha256Hash[..12] : Detail.Sha256Hash);

    public bool IsScheduleSelected => string.Equals(SelectedAction, "Schedule", StringComparison.Ordinal);

    public bool IsActivateSelected => string.Equals(SelectedAction, "Activate", StringComparison.Ordinal);

    public bool IsDeprecateSelected => string.Equals(SelectedAction, "Deprecate", StringComparison.Ordinal);

    public bool IsRetireSelected => string.Equals(SelectedAction, "Retire", StringComparison.Ordinal);

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
