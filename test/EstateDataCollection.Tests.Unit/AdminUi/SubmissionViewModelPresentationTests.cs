using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionDetail;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class SubmissionViewModelPresentationTests
{
    private static AdminPageMeta CreateMeta(string activeNav)
    {
        return new AdminPageMeta("Title", activeNav, "admin@education.gov.uk", "2026.11.20");
    }

    [Fact]
    public void StatisticsViewModel_And_SubmissionsViewModel_Should_Project_Tags_And_Links()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var summary = new SubmissionSummary(
            id, "cdc_survey", 2, now, null, now, false, "Draft", 104521, "URN", "104521",
            true, 2, now, 3, "CheckSubmission", "ReadyToFinalise", 200, now);

        var statsResult = new GetSubmissionStatisticsResult(
            TotalSubmissions: 1,
            DraftSubmissions: 1,
            DraftWithPayloadSubmissions: 1,
            EmptyDraftSubmissions: 0,
            FinalisedSubmissions: 0,
            SubmissionsLast24Hours: 1,
            SubmissionsLast7Days: 1,
            TotalDraftChunks: 2,
            TotalAuditEvents: 3,
            FinalisationRatePercent: 0,
            SchemaStatistics:
            [
                new("cdc_survey", 2, "Active", 1, 1, 1, 0, 0, 2, 3, 0, now)
            ],
            RecentSubmissions: [summary],
            AvailableSchemaNames: ["cdc_survey"]);

        var statsVm = new StatisticsViewModel(CreateMeta("statistics"), statsResult);
        statsVm.Page.StatisticsNavLinkCssClass.Should().Contain("dfe-service-nav__link--active");
        statsVm.SchemaRows[0].DraftSubmissionsUrl.Should().Be("/manage/submissions?schemaName=cdc_survey&version=2&status=Draft");
        statsVm.RecentSubmissions[0].DetailUrl.Should().Be($"/manage/submissions/{id}");
        statsVm.RecentSubmissions[0].LifecycleTagCssClass.Should().Contain("govuk-tag--blue");

        var listVm = new SubmissionsViewModel(
            CreateMeta("submissions"),
            new GetSubmissionsResult([summary], ["cdc_survey"], "cdc_survey", 2, "Draft", null));
        listVm.HasActiveFilters.Should().BeTrue();
        listVm.DraftCount.Should().Be(1);
        listVm.Items[0].LastAuditTagCssClass.Should().Contain("govuk-tag--green");
    }

    [Fact]
    public void SubmissionDetailViewModel_Should_Format_Draft_Chunks_And_Audit_Processes()
    {
        var session = SubmissionSession.Create("edc_payload", 1);
        session.AssignUrn(123456);
        var now = DateTimeOffset.UtcNow;

        var detail = new GetSubmissionDetailResult(
            Session: session,
            RawSessionJson: """{"submissionId":"1"}""",
            DraftItems: [new SubmissionDraftItem($"{session.SubmissionId}/drafts/01.json", now, """{"urn":123456}""", 14)],
            MergedDraftPayloadJson: """{"urn":123456}""",
            ProcessedArtifact: null,
            IsReadyToFinalise: false,
            PendingErrors: [new ValidationError("#/school_name", "Required", "REQ-1")],
            NextSteps: ["Resolve [REQ-1] #/school_name: Required"],
            NextStepsSummary: "1 pending issue",
            ResolvedIdentifierType: "URN",
            ResolvedIdentifierValue: "123456",
            ResolvedUrn: 123456,
            AuditEntries:
            [
                new SubmissionAuditEntry
                {
                    EventId = Guid.CreateVersion7(),
                    TimestampUtc = now,
                    SubmissionId = session.SubmissionId,
                    Action = "CheckSubmission",
                    Outcome = new SubmissionAuditOutcome { Status = "DraftPendingErrors", HttpStatus = 200 },
                    ProcessesConsumed =
                    [
                        new SubmissionProcessExecution { ProcessName = "DraftPayloadMerger", Engine = "rfc7396-json-merge", Outcome = "Succeeded", DurationMs = 2, ItemCount = 1, Summary = "Merged 1 chunk" }
                    ]
                }
            ]);

        var vm = new SubmissionDetailViewModel(CreateMeta("submissions"), detail);

        vm.IdentifierDisplay.Should().Be("URN: 123456");
        vm.FormattedMergedDraftPayload.Should().Contain("\"urn\": 123456");
        vm.DraftChunks[0].ShortBlobName.Should().Be("01.json");
        vm.AuditItems[0].Processes[0].ProcessName.Should().Be("DraftPayloadMerger");
        vm.AuditItems[0].Processes[0].OutcomeTagCssClass.Should().Contain("govuk-tag--green");
    }
}
