using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionDetail;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Routing;
using EstateDataCollection.Domain.Submission;

namespace EstateDataCollection.Admin.UI.Models.ViewModels;

public sealed record SubmissionDetailViewModel(
    AdminPageMeta Page,
    GetSubmissionDetailResult? Detail,
    Error? PageError = null)
{
    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions AuditPrettyOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ErrorSummaryViewModel? ErrorSummary
    {
        get { return ErrorSummaryViewModel.From(PageError); }
    }

    public bool HasDetail
    {
        get { return Detail is not null; }
    }

    public string SubmissionIdDisplay
    {
        get { return Detail?.Session.SubmissionId.ToString() ?? string.Empty; }
    }

    public string SchemaName
    {
        get { return Detail?.Session.SchemaName ?? string.Empty; }
    }

    public int SchemaVersion
    {
        get { return Detail?.Session.SchemaVersion ?? 0; }
    }

    public string SchemaVersionDisplay
    {
        get { return HasDetail ? $"{SchemaName} v{SchemaVersion}" : string.Empty; }
    }

    public string SchemaDetailUrl
    {
        get
        {
            return HasDetail
                ? AppRoutes.AdminUi.SchemaDetail(SchemaName, SchemaVersion)
                : AppRoutes.AdminUi.Dashboard;
        }
    }

    public string SchemaSubmissionsUrl
    {
        get
        {
            return HasDetail
                ? AppRoutes.AdminUi.SubmissionsFiltered(SchemaName, SchemaVersion)
                : AppRoutes.AdminUi.Submissions;
        }
    }

    public bool IsFinalised
    {
        get { return Detail?.Session.IsFinalised ?? false; }
    }

    public string LifecycleTagText
    {
        get
        {
            return IsFinalised
                ? "Finalised"
                : Detail?.DraftItems.Count > 0 || !string.IsNullOrWhiteSpace(Detail?.MergedDraftPayloadJson)
                    ? "Draft (payload saved)"
                    : "Draft (awaiting payload)";
        }
    }

    public string LifecycleTagCssClass
    {
        get
        {
            return IsFinalised
                ? "govuk-tag govuk-tag--green"
                : Detail?.DraftItems.Count > 0 || !string.IsNullOrWhiteSpace(Detail?.MergedDraftPayloadJson)
                    ? "govuk-tag govuk-tag--blue"
                    : "govuk-tag govuk-tag--grey";
        }
    }

    public bool IsReadyToFinalise
    {
        get { return Detail?.IsReadyToFinalise ?? false; }
    }

    public string ReadinessTagText
    {
        get
        {
            return IsFinalised
                ? "Locked (Finalised)"
                : IsReadyToFinalise
                    ? "Ready to finalise"
                    : "Not ready (pending issues)";
        }
    }

    public string ReadinessTagCssClass
    {
        get
        {
            return IsFinalised
                ? "govuk-tag govuk-tag--green"
                : IsReadyToFinalise
                    ? "govuk-tag govuk-tag--green"
                    : "govuk-tag govuk-tag--yellow";
        }
    }

    public string CreatedAtDisplay
    {
        get
        {
            return Detail is not null
                ? Detail.Session.CreatedAtUtc.ToUniversalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                : "—";
        }
    }

    public string FinalisedAtDisplay
    {
        get
        {
            return Detail?.Session.FinalisedAtUtc is { } finalisedAt
                ? finalisedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                : "—";
        }
    }

    public string IdentifierDisplay
    {
        get
        {
            if (Detail is null)
            {
                return "—";
            }

            if (!string.IsNullOrWhiteSpace(Detail.ResolvedIdentifierValue))
            {
                return !string.IsNullOrWhiteSpace(Detail.ResolvedIdentifierType)
                    ? $"{Detail.ResolvedIdentifierType}: {Detail.ResolvedIdentifierValue}"
                    : Detail.ResolvedIdentifierValue;
            }

            if (Detail.ResolvedUrn.HasValue)
            {
                return $"URN: {Detail.ResolvedUrn.Value.ToString(CultureInfo.InvariantCulture)}";
            }

            return "Unassigned";
        }
    }

    public int DraftCount
    {
        get { return Detail?.DraftItems.Count ?? 0; }
    }

    public int AuditCount
    {
        get { return Detail?.AuditEntries.Count ?? 0; }
    }

    public string NextStepsSummary
    {
        get { return Detail?.NextStepsSummary ?? string.Empty; }
    }

    public IReadOnlyList<string> NextSteps
    {
        get { return Detail?.NextSteps ?? []; }
    }

    public bool HasNextSteps
    {
        get { return NextSteps.Count > 0; }
    }

    public IReadOnlyList<ValidationErrorItemViewModel> PendingErrors
    {
        get
        {
            return _pendingErrors ??= (Detail?.PendingErrors ?? [])
                .Select(e => new ValidationErrorItemViewModel(
                    HasPropertyPath: !string.IsNullOrWhiteSpace(e.PropertyPath),
                    PropertyPath: e.PropertyPath,
                    Message: !string.IsNullOrWhiteSpace(e.RuleId) ? $"[{e.RuleId}] {e.Message}" : e.Message))
                .ToList();
        }
    }

    private IReadOnlyList<ValidationErrorItemViewModel>? _pendingErrors;

    public bool HasPendingErrors
    {
        get { return PendingErrors.Count > 0; }
    }

    public bool HasMergedDraftPayload
    {
        get { return !string.IsNullOrWhiteSpace(Detail?.MergedDraftPayloadJson); }
    }

    public string FormattedMergedDraftPayload
    {
        get { return FormatJson(Detail?.MergedDraftPayloadJson); }
    }

    public bool HasProcessedArtifact
    {
        get { return Detail?.ProcessedArtifact is not null; }
    }

    public string ProcessedBlobName
    {
        get { return Detail?.ProcessedArtifact?.BlobName ?? string.Empty; }
    }

    public string ProcessedSha256
    {
        get { return Detail?.ProcessedArtifact?.Sha256Hash ?? "—"; }
    }

    public string ProcessedStatus
    {
        get { return Detail?.ProcessedArtifact?.Status ?? "ReadyForCde"; }
    }

    public string ProcessedSizeDisplay
    {
        get { return Detail?.ProcessedArtifact is { } art ? FormatBytes(art.SizeBytes) : "—"; }
    }

    public string FormattedProcessedPayload
    {
        get { return FormatJson(Detail?.ProcessedArtifact?.PayloadJson); }
    }

    public string RawSessionJson
    {
        get { return Detail?.RawSessionJson ?? "{}"; }
    }

    public IReadOnlyList<SubmissionDraftChunkViewModel> DraftChunks
    {
        get
        {
            return _draftChunks ??= (Detail?.DraftItems ?? [])
                .Select((item, idx) =>
                {
                    var fileName = Path.GetFileName(item.BlobName);
                    return new SubmissionDraftChunkViewModel(
                        Index: idx + 1,
                        BlobName: item.BlobName,
                        ShortBlobName: string.IsNullOrWhiteSpace(fileName) ? item.BlobName : fileName,
                        CreatedAtDisplay: item.CreatedAtUtc.ToUniversalTime()
                            .ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture),
                        SizeDisplay: FormatBytes(item.SizeBytes),
                        FormattedJson: FormatJson(item.PayloadJson));
                })
                .ToList();
        }
    }

    private IReadOnlyList<SubmissionDraftChunkViewModel>? _draftChunks;

    public bool HasDraftChunks
    {
        get { return DraftChunks.Count > 0; }
    }

    public IReadOnlyList<SubmissionAuditItemViewModel> AuditItems
    {
        get
        {
            return _auditItems ??= (Detail?.AuditEntries ?? [])
                .Select((entry, idx) =>
                {
                    var outcomeCss = entry.Outcome.Status.ToLowerInvariant() switch
                    {
                        "succeeded" or "readytofinalise" or "finalised" => "govuk-tag govuk-tag--green",
                        "savedwithpendingerrors" or "draftpendingerrors" or "draftawaitingpayload" =>
                            "govuk-tag govuk-tag--yellow",
                        "rejected" or "failed" => "govuk-tag govuk-tag--red",
                        _ => "govuk-tag govuk-tag--grey"
                    };

                    var processes = entry.ProcessesConsumed
                        .Select(p =>
                        {
                            var procCss = p.Outcome switch
                            {
                                SubmissionProcessOutcome.Succeeded => "govuk-tag govuk-tag--green",
                                SubmissionProcessOutcome.Skipped
                                    or SubmissionProcessOutcome.Missing
                                    or SubmissionProcessOutcome.ValidationErrorsFound => "govuk-tag govuk-tag--yellow",
                                SubmissionProcessOutcome.Failed => "govuk-tag govuk-tag--red",
                                _ => "govuk-tag govuk-tag--grey"
                            };

                            var processErrors = (p.Errors ?? [])
                                .Select(ve => new ValidationErrorItemViewModel(
                                    HasPropertyPath: !string.IsNullOrWhiteSpace(ve.PropertyPath),
                                    PropertyPath: ve.PropertyPath,
                                    Message: !string.IsNullOrWhiteSpace(ve.RuleId)
                                        ? $"[{ve.RuleId}] {ve.Message}"
                                        : ve.Message))
                                .ToList();

                            return new AuditProcessRowViewModel(
                                ProcessName: p.ProcessName,
                                EngineDisplay: string.IsNullOrWhiteSpace(p.Engine) ? "—" : p.Engine,
                                OutcomeText: p.Outcome.ToString(),
                                OutcomeTagCssClass: procCss,
                                DurationDisplay: $"{p.DurationMs} ms",
                                ItemCountDisplay: p.ItemCount?.ToString(CultureInfo.InvariantCulture) ?? "—",
                                SummaryDisplay: string.IsNullOrWhiteSpace(p.Summary) ? "—" : p.Summary,
                                ErrorCode: string.IsNullOrWhiteSpace(p.ErrorCode) ? null : p.ErrorCode,
                                Errors: processErrors.Count > 0 ? processErrors : null);
                        })
                        .ToList();

                    var issues = (entry.Outcome.ValidationErrors ?? [])
                        .Select(ve => new ValidationErrorItemViewModel(
                            HasPropertyPath: !string.IsNullOrWhiteSpace(ve.PropertyPath),
                            PropertyPath: ve.PropertyPath,
                            Message: !string.IsNullOrWhiteSpace(ve.RuleId)
                                ? $"[{ve.RuleId}] {ve.Message}"
                                : ve.Message))
                        .ToList();

                    var hasId = entry.Identifier is not null &&
                                (!string.IsNullOrWhiteSpace(entry.Identifier.Value) || entry.Identifier.Urn.HasValue);
                    var idText = entry.Identifier is not null && hasId
                        ? !string.IsNullOrWhiteSpace(entry.Identifier.Value)
                            ? $"{entry.Identifier.Type ?? "ID"}: {entry.Identifier.Value}"
                            : $"URN: {entry.Identifier.Urn}"
                        : string.Empty;

                    return new SubmissionAuditItemViewModel(
                        EventId: entry.EventId,
                        SequenceNumber: idx + 1,
                        TimestampDisplay: entry.TimestampUtc.ToUniversalTime()
                            .ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture),
                        Action: entry.Action,
                        OutcomeStatusText: entry.Outcome.Status,
                        OutcomeTagCssClass: outcomeCss,
                        HttpStatusDisplay: entry.Outcome.HttpStatus > 0 ? $"HTTP {entry.Outcome.HttpStatus}" : "—",
                        HasMessage: !string.IsNullOrWhiteSpace(entry.Outcome.Message),
                        Message: entry.Outcome.Message ?? string.Empty,
                        HasErrorCode: !string.IsNullOrWhiteSpace(entry.Outcome.ErrorCode),
                        ErrorCode: entry.Outcome.ErrorCode ?? string.Empty,
                        HasTraceId: !string.IsNullOrWhiteSpace(entry.TraceId),
                        TraceId: entry.TraceId ?? string.Empty,
                        HasIdentifier: hasId,
                        IdentifierDisplay: idText,
                        Processes: processes,
                        HasProcesses: processes.Count > 0,
                        ValidationIssues: issues,
                        HasValidationIssues: issues.Count > 0,
                        RawAuditJson: JsonSerializer.Serialize(entry, AuditPrettyOptions));
                })
                .ToList();
        }
    }

    private IReadOnlyList<SubmissionAuditItemViewModel>? _auditItems;

    public bool HasAuditItems
    {
        get { return AuditItems.Count > 0; }
    }

    public GovUkTabsViewModel Tabs
    {
        get
        {
            return _tabs ??= GovUkTabsViewModel.Create(
                "submission-detail-tabs",
                [
                    ("submission-audit-trail", $"Audit trail ({AuditCount})", true),
                    ("submission-payload-session", "Payload & session", true),
                    ("submission-draft-uploads", $"Draft uploads ({DraftCount})", true)
                ]);
        }
    }

    private GovUkTabsViewModel? _tabs;

    public GovUkTabItemViewModel? AuditTrailTab
    {
        get { return Tabs.Find("submission-audit-trail"); }
    }

    public GovUkTabItemViewModel? PayloadSessionTab
    {
        get { return Tabs.Find("submission-payload-session"); }
    }

    public GovUkTabItemViewModel? DraftUploadsTab
    {
        get { return Tabs.Find("submission-draft-uploads"); }
    }

    private static string FormatJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, PrettyOptions);
        }
        catch
        {
            return raw;
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes < 1024
            ? $"{bytes} B"
            : $"{bytes / 1024.0:F1} KB";
    }
}
