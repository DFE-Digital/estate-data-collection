using EstateDataCollection.Application.Models.Contracts;
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
    public ErrorSummaryViewModel? ErrorSummary
    {
        get { return ErrorSummaryViewModel.From(PageError); }
    }

    public string BackToDetailsUrl
    {
        get { return AppRoutes.AdminUi.SchemaDetail(SchemaName, Version); }
    }

    public string BackToDetailsText
    {
        get { return $"Back to {SchemaName} V{Version}"; }
    }

    public string HeadingText
    {
        get { return $"Manage {SchemaName}, V{Version}"; }
    }

    public string FormActionUrl
    {
        get { return AppRoutes.AdminUi.SchemaSchedule(SchemaName, Version); }
    }

    public bool HasDetail
    {
        get { return Detail is not null; }
    }

    public LifecycleTagViewModel? DetailStateTag
    {
        get { return Detail is null ? null : LifecycleTagViewModel.From(Detail.State.Name); }
    }

    public string ShortSha256Hash
    {
        get
        {
            return Detail is null
                ? string.Empty
                : (Detail.Sha256Hash.Length > 12 ? Detail.Sha256Hash[..12] : Detail.Sha256Hash);
        }
    }

    public bool IsScheduleSelected
    {
        get { return string.Equals(SelectedAction, "Schedule", StringComparison.Ordinal); }
    }

    public bool IsActivateSelected
    {
        get { return string.Equals(SelectedAction, "Activate", StringComparison.Ordinal); }
    }

    public bool IsDeprecateSelected
    {
        get { return string.Equals(SelectedAction, "Deprecate", StringComparison.Ordinal); }
    }

    public bool IsRetireSelected
    {
        get { return string.Equals(SelectedAction, "Retire", StringComparison.Ordinal); }
    }

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
