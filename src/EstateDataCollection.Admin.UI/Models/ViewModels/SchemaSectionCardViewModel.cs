using EstateDataCollection.Application.Models.Contracts;

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
        var escapedSchema = Uri.EscapeDataString(entry.SchemaName);
        var versions = entry.Versions
            .OrderBy(x => x.Version)
            .Select(v =>
            {
                var hasIdConfig = v.IdentifierConfig is not null;
                var isIdDisabled = hasIdConfig &&
                    string.Equals(v.IdentifierConfig!.Type, "None", StringComparison.OrdinalIgnoreCase);
                var hasCustomId = hasIdConfig && !isIdDisabled;
                var escapedVersionSchema = Uri.EscapeDataString(v.SchemaName);

                var (testStatusTagText, testStatusTagCssClass) = ResolveTestStatus(v);
                var hasTimestamp = v.LastTestRunAtUtc.HasValue;
                var timestampDisplay = hasTimestamp
                    ? v.LastTestRunAtUtc!.Value.ToString("yyyy-MM-dd HH:mm 'UTC'")
                    : string.Empty;

                return new SchemaVersionRowViewModel(
                    Version: v.Version,
                    StateTag: LifecycleTagViewModel.From(v.State.Name),
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
                    DetailsUrl: $"/manage/schemas/{escapedVersionSchema}/{v.Version}/details",
                    TestsUrl: $"/manage/schemas/{escapedVersionSchema}/{v.Version}/tests",
                    ScheduleUrl: $"/manage/schemas/{escapedVersionSchema}/{v.Version}/schedule",
                    CanModifyLifecycle: !string.Equals(v.State.Name, "Retired", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        return new SchemaSectionCardViewModel(
            SchemaName: entry.SchemaName,
            HasActiveVersion: entry.ActiveVersion.HasValue,
            NoActiveVersion: !entry.ActiveVersion.HasValue,
            ActiveVersionDisplay: entry.ActiveVersion.HasValue ? $"v{entry.ActiveVersion}" : string.Empty,
            ManageUrl: $"/manage/schemas/{escapedSchema}/",
            EnrichedView: enrichedView,
            Versions: versions);
    }

    private static (string TagText, string TagCssClass) ResolveTestStatus(SchemaVersionSummary v)
    {
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
