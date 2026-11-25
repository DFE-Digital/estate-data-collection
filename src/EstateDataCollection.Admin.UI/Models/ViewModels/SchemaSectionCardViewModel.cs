using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Routing;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SchemaSectionCardViewModel(
    string SchemaName,
    bool HasActiveVersion,
    bool NoActiveVersion,
    string ActiveVersionDisplay,
    string ManageUrl,
    bool EnrichedView,
    IReadOnlyList<SchemaVersionRowViewModel> Versions)
{
    public static SchemaSectionCardViewModel From(SchemaCatalogueEntry entry, bool enrichedView = false)
    {
        var versions = entry.Versions
            .OrderBy(x => x.Version)
            .Select(v =>
            {
                var hasIdConfig = v.IdentifierConfig is not null;
                var isIdDisabled = hasIdConfig &&
                    string.Equals(v.IdentifierConfig!.Type, "None", StringComparison.OrdinalIgnoreCase);
                var hasCustomId = hasIdConfig && !isIdDisabled;

                var stateName = v.State.Name;
                var isRetired = string.Equals(stateName, "Retired", StringComparison.OrdinalIgnoreCase);
                var isDeleted = string.Equals(stateName, "Deleted", StringComparison.OrdinalIgnoreCase) || v.DeletedAtUtc.HasValue;
                var isInvalid = string.Equals(stateName, "Invalid", StringComparison.OrdinalIgnoreCase);
                var isUnvalidated = string.Equals(stateName, "Unvalidated", StringComparison.OrdinalIgnoreCase);

                var (testStatusTagText, testStatusTagCssClass) = ResolveTestStatus(v, isDeleted);
                var hasTimestamp = v.LastTestRunAtUtc.HasValue;
                var timestampDisplay = hasTimestamp
                    ? v.LastTestRunAtUtc!.Value.ToString("yyyy-MM-dd HH:mm 'UTC'")
                    : string.Empty;

                var errorTagText = isInvalid
                    ? ".error.json"
                    : v.HasErrorReport
                        ? "Warnings (.error.json)"
                        : string.Empty;
                var errorTagCss = isInvalid
                    ? "govuk-tag govuk-tag--red"
                    : "govuk-tag govuk-tag--yellow";

                var hasSignedOff = !string.IsNullOrWhiteSpace(v.SignedOffBy);
                var signedOffDisplay = hasSignedOff
                    ? (v.SignedOffAtUtc.HasValue
                        ? $"Signed off by {v.SignedOffBy} ({v.SignedOffAtUtc.Value:yyyy-MM-dd HH:mm} UTC)"
                        : $"Signed off by {v.SignedOffBy}")
                    : string.Empty;

                var deletedDisplay = v.DeletedAtUtc.HasValue
                    ? $"Deleted {v.DeletedAtUtc.Value:yyyy-MM-dd HH:mm} UTC (push fixes to v{v.Version + 1})"
                    : (isDeleted ? $"Deleted (push fixes to v{v.Version + 1})" : string.Empty);

                return new SchemaVersionRowViewModel(
                    Version: v.Version,
                    StateTag: LifecycleTagViewModel.From(stateName),
                    TestStatusTagText: testStatusTagText,
                    TestStatusTagCssClass: testStatusTagCssClass,
                    HasTestRunTimestamp: hasTimestamp,
                    TestRunTimestampDisplay: timestampDisplay,
                    EffectiveFromDisplay: v.EffectiveFromUtc?.ToString("yyyy-MM-dd HH:mm") ?? "Unscheduled",
                    EffectiveToDisplay: v.EffectiveToUtc?.ToString("yyyy-MM-dd HH:mm") ?? "Open-ended",
                    HasIdentifierConfig: hasIdConfig,
                    IsIdentifierDisabled: isIdDisabled,
                    HasCustomIdentifier: hasCustomId,
                    IdentifierType: v.IdentifierConfig?.Type ?? string.Empty,
                    IdentifierPointer: v.IdentifierConfig?.JsonPointer ?? string.Empty,
                    ShortSha256Hash: v.Sha256Hash.Length > 10 ? v.Sha256Hash[..10] : v.Sha256Hash,
                    DetailsUrl: AppRoutes.AdminUi.SchemaDetail(v.SchemaName, v.Version),
                    TestsUrl: AppRoutes.AdminUi.SchemaTests(v.SchemaName, v.Version),
                    ScheduleUrl: AppRoutes.AdminUi.SchemaSchedule(v.SchemaName, v.Version),
                    CanModifyLifecycle: !isRetired && !isDeleted && !isInvalid && !isUnvalidated,
                    CanRunTests: !isDeleted,
                    HasErrorReport: v.HasErrorReport,
                    ErrorReportTagText: errorTagText,
                    ErrorReportTagCssClass: errorTagCss,
                    ErrorSummary: v.ErrorSummary ?? string.Empty,
                    HasSignedOff: hasSignedOff,
                    SignedOffDisplay: signedOffDisplay,
                    IsDeleted: isDeleted,
                    DeletedDisplay: deletedDisplay);
            })
            .ToList();

        return new SchemaSectionCardViewModel(
            SchemaName: entry.SchemaName,
            HasActiveVersion: entry.ActiveVersion.HasValue,
            NoActiveVersion: !entry.ActiveVersion.HasValue,
            ActiveVersionDisplay: entry.ActiveVersion.HasValue ? $"v{entry.ActiveVersion}" : string.Empty,
            ManageUrl: AppRoutes.AdminUi.SchemaVersions(entry.SchemaName),
            EnrichedView: enrichedView,
            Versions: versions);
    }

    private static (string TagText, string TagCssClass) ResolveTestStatus(SchemaVersionSummary v, bool isDeleted)
    {
        if (isDeleted)
        {
            return ("Tombstone", "govuk-tag govuk-tag--grey");
        }

        if (v.LastTestRunPassed is null && !v.LastTestRunAtUtc.HasValue)
        {
            return ("Not run", "govuk-tag govuk-tag--grey");
        }

        if (v.LastTestRunPassed == false)
        {
            return ("Failed", "govuk-tag govuk-tag--red");
        }

        if (v.LastTestRunWarnings is { Count: > 0 })
        {
            return ("Passed (warnings)", "govuk-tag govuk-tag--yellow");
        }

        return ("Passed", "govuk-tag govuk-tag--green");
    }
}
