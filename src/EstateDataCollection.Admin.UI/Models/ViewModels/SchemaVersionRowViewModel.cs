namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaVersionRowViewModel(
    int Version,
    LifecycleTagViewModel StateTag,
    string TestStatusTagText,
    string TestStatusTagCssClass,
    bool HasTestRunTimestamp,
    string TestRunTimestampDisplay,
    string EffectiveFromDisplay,
    string EffectiveToDisplay,
    bool HasIdentifierConfig,
    bool IsIdentifierDisabled,
    bool HasCustomIdentifier,
    string IdentifierType,
    string IdentifierPointer,
    string ShortSha256Hash,
    string DetailsUrl,
    string TestsUrl,
    string ScheduleUrl,
    bool CanModifyLifecycle);