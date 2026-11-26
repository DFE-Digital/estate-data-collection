using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissions;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
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
        var summary = new SubmissionSummaryBuilder()
            .WithId(id)
            .WithSchema("cdc_survey", 2)
            .CreatedAt(now)
            .LastActivityAt(now)
            .WithIdentifier("URN", "104521")
            .WithDrafts(count: 2, lastDraftAtUtc: now)
            .WithLastAudit("CheckSubmission", "ReadyToFinalise", 200, now, totalEvents: 3)
            .Build();

        var statsResult = new GetSubmissionStatisticsResultBuilder()
            .WithSubmissionCounts(total: 1, draft: 1, draftWithPayload: 1, emptyDraft: 0, finalised: 0)
            .WithRecentActivity(last24Hours: 1, last7Days: 1)
            .WithArtefactTotals(totalDraftChunks: 2, totalAuditEvents: 3)
            .WithSchemaStatistic(
                "cdc_survey",
                version: 2,
                state: "Active",
                totalSubmissions: 1,
                draftSubmissions: 1,
                draftWithPayloadSubmissions: 1,
                totalDraftChunks: 2,
                totalAuditEvents: 3,
                lastActivityAtUtc: now)
            .WithRecentSubmission(summary)
            .WithAvailableSchemas("cdc_survey")
            .Build();

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
        session.AssignIdentifier("URN", "123456");
        var now = DateTimeOffset.UtcNow;

        var detail = new GetSubmissionDetailResultBuilder()
            .WithSession(session)
            .WithDraftItem($"{session.SubmissionId}/drafts/01.json", """{"urn":123456}""", now, sizeBytes: 14)
            .WithMergedDraftPayload("""{"urn":123456}""")
            .ReadyToFinalise(false)
            .WithPendingError("#/school_name", "Required", "REQ-1")
            .WithNextSteps("1 pending issue", "Resolve [REQ-1] #/school_name: Required")
            .WithIdentifier("URN", "123456")
            .WithAuditEntry(entry => entry
                .WithTimestamp(now)
                .WithAction("CheckSubmission")
                .WithActor("Acme Surveys (sub-acme-01)")
                .WithOutcome("DraftPendingErrors", 200)
                .WithProcess(
                    SubmissionProcessCatalog.DraftPayloadMerger,
                    p => p.WithDurationMs(2).WithItemCount(1).WithSummary("Merged 1 chunk"))
                .WithProcess(
                    SubmissionProcessCatalog.QualityRulesValidator,
                    p => p.WithOutcome(SubmissionProcessOutcome.ValidationErrorsFound)
                        .WithDurationMs(4)
                        .WithItemCount(1)
                        .WithSummary("Evaluated 1 quality rule(s); 1 violation(s)")
                        .WithErrorCode("Validation.QualityRuleFailed")
                        .WithError("$.school_capacity", "Capacity must be positive", "RULE-CAP")))
            .Build();

        var vm = new SubmissionDetailViewModel(CreateMeta("submissions"), detail);

        vm.IdentifierDisplay.Should().Be("URN: 123456");
        vm.FormattedMergedDraftPayload.Should().Contain("\"urn\": 123456");
        vm.DraftChunks[0].ShortBlobName.Should().Be("01.json");
        vm.AuditItems[0].HasActor.Should().BeTrue();
        vm.AuditItems[0].ActorDisplay.Should().Be("Acme Surveys (sub-acme-01)");
        vm.AuditItems[0].Processes[0].ProcessName.Should().Be("DraftPayloadMerger");
        vm.AuditItems[0].Processes[0].OutcomeTagCssClass.Should().Contain("govuk-tag--green");
        vm.AuditItems[0].Processes[0].HasErrorCode.Should().BeFalse();
        vm.AuditItems[0].Processes[0].HasErrors.Should().BeFalse();

        var ruleProcess = vm.AuditItems[0].Processes[1];
        ruleProcess.HasErrorCode.Should().BeTrue();
        ruleProcess.ErrorCode.Should().Be("Validation.QualityRuleFailed");
        ruleProcess.HasErrors.Should().BeTrue();
        ruleProcess.Errors.Should().ContainSingle();
        ruleProcess.Errors![0].HasPropertyPath.Should().BeTrue();
        ruleProcess.Errors[0].PropertyPath.Should().Be("$.school_capacity");
        ruleProcess.Errors[0].Message.Should().Be("[RULE-CAP] Capacity must be positive");
    }
}
