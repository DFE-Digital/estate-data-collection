using System.Globalization;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SubmissionListItemViewModel(
    Guid SubmissionId,
    string ShortSubmissionId,
    string SchemaName,
    int SchemaVersion,
    string SchemaVersionDisplay,
    string DetailUrl,
    string SchemaDetailUrl,
    string LifecycleStateText,
    string LifecycleTagCssClass,
    bool HasIdentifier,
    string IdentifierDisplay,
    int DraftCount,
    int AuditEventCount,
    bool HasLastAuditOutcome,
    string LastAuditSummaryDisplay,
    string LastAuditTagCssClass,
    string CreatedAtDisplay,
    string LastActivityDisplay)
{
    public static SubmissionListItemViewModel From(SubmissionSummary summary)
    {
        var (stateText, stateCss) = summary.LifecycleState switch
        {
            "Finalised" => ("Finalised", "govuk-tag govuk-tag--green"),
            "Draft" => ("Draft (payload saved)", "govuk-tag govuk-tag--blue"),
            "AwaitingPayload" => ("Draft (awaiting payload)", "govuk-tag govuk-tag--grey"),
            _ => (summary.LifecycleState, "govuk-tag govuk-tag--grey")
        };

        var hasIdentifier = !string.IsNullOrWhiteSpace(summary.IdentifierValue) || summary.Urn.HasValue;
        var identifierDisplay = !string.IsNullOrWhiteSpace(summary.IdentifierValue)
            ? (!string.IsNullOrWhiteSpace(summary.IdentifierType)
                ? $"{summary.IdentifierType}: {summary.IdentifierValue}"
                : summary.IdentifierValue)
            : summary.Urn.HasValue
                ? $"URN: {summary.Urn.Value.ToString(CultureInfo.InvariantCulture)}"
                : "Unassigned";

        var hasAudit = !string.IsNullOrWhiteSpace(summary.LastAuditOutcome);
        var auditSummary = hasAudit
            ? $"{summary.LastAuditAction}: {summary.LastAuditOutcome}" +
              (summary.LastAuditHttpStatus.HasValue ? $" ({summary.LastAuditHttpStatus.Value})" : string.Empty)
            : "None";

        var auditCss = (summary.LastAuditOutcome ?? string.Empty).ToLowerInvariant() switch
        {
            "succeeded" or "readytofinalise" or "finalised" => "govuk-tag govuk-tag--green",
            "savedwithpendingerrors" or "draftpendingerrors" or "draftawaitingpayload" => "govuk-tag govuk-tag--yellow",
            "rejected" or "failed" => "govuk-tag govuk-tag--red",
            _ => "govuk-tag govuk-tag--grey"
        };

        var idText = summary.SubmissionId.ToString();
        var shortId = idText.Length >= 13 ? $"{idText[..8]}…{idText[^4..]}" : idText;

        return new SubmissionListItemViewModel(
            SubmissionId: summary.SubmissionId,
            ShortSubmissionId: shortId,
            SchemaName: summary.SchemaName,
            SchemaVersion: summary.SchemaVersion,
            SchemaVersionDisplay: $"{summary.SchemaName} v{summary.SchemaVersion}",
            DetailUrl: AppRoutes.AdminUi.SubmissionDetail(summary.SubmissionId),
            SchemaDetailUrl: AppRoutes.AdminUi.SchemaDetail(summary.SchemaName, summary.SchemaVersion),
            LifecycleStateText: stateText,
            LifecycleTagCssClass: stateCss,
            HasIdentifier: hasIdentifier,
            IdentifierDisplay: identifierDisplay,
            DraftCount: summary.DraftCount,
            AuditEventCount: summary.AuditEventCount,
            HasLastAuditOutcome: hasAudit,
            LastAuditSummaryDisplay: auditSummary,
            LastAuditTagCssClass: auditCss,
            CreatedAtDisplay: summary.CreatedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            LastActivityDisplay: summary.LastActivityAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
    }
}
