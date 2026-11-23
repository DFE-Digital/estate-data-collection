using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionDetail;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatistics;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class GetSubmissionStatisticsAndDetailUseCaseTests
{
    [Fact]
    public async Task GetSubmissionStatistics_Should_Aggregate_Draft_And_Finalised_Counts_Per_Schema()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var queryRepo = Substitute.For<ISubmissionQueryRepository>();

        var now = DateTimeOffset.UtcNow;
        var s1v1 = new SchemaMetadataBuilder().WithName("school_site").WithVersion(1).Build();
        var s2v1 = new SchemaMetadataBuilder().WithName("empty_schema").WithVersion(1).Build();

        registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([s1v1, s2v1]));

        IReadOnlyList<SubmissionSummary> summaries =
        [
            new SubmissionSummaryBuilder()
                .WithSchema("school_site", 1)
                .CreatedAt(now.AddHours(-2))
                .LastActivityAt(now.AddHours(-1))
                .WithLifecycleState("Draft")
                .WithUrn(100001)
                .WithDrafts(3, now.AddHours(-1))
                .WithLastAudit("UpdateSubmission", "SavedWithPendingErrors", 200, now.AddHours(-1), totalEvents: 4)
                .Build(),
            new SubmissionSummaryBuilder()
                .WithSchema("school_site", 1)
                .CreatedAt(now.AddDays(-3))
                .LastActivityAt(now.AddDays(-3))
                .WithLifecycleState("AwaitingPayload")
                .WithDrafts(0, null, hasDraftPayload: false)
                .WithLastAudit("CreateSubmission", "Succeeded", 201, now.AddDays(-3), totalEvents: 1)
                .Build(),
            new SubmissionSummaryBuilder()
                .WithSchema("school_site", 1)
                .CreatedAt(now.AddHours(-5))
                .LastActivityAt(now.AddHours(-2))
                .Finalised(now.AddHours(-2))
                .WithUrn(100002)
                .WithDrafts(2, now.AddHours(-3), hasDraftPayload: false)
                .WithLastAudit("FinaliseSubmission", "Succeeded", 200, now.AddHours(-2), totalEvents: 4)
                .Build()
        ];

        queryRepo.ListSubmissionsAsync(null, null, null, null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SubmissionSummary>>.Success(summaries));

        var sut = new GetSubmissionStatisticsUseCase(registry, queryRepo);
        var stats = (await sut.ExecuteAsync(new GetSubmissionStatisticsRequest())).ShouldSucceed();

        stats.TotalSubmissions.Should().Be(3);
        stats.DraftSubmissions.Should().Be(2);
        stats.DraftWithPayloadSubmissions.Should().Be(1);
        stats.EmptyDraftSubmissions.Should().Be(1);
        stats.FinalisedSubmissions.Should().Be(1);
        stats.SubmissionsLast24Hours.Should().Be(2);
        stats.SubmissionsLast7Days.Should().Be(3);
        stats.TotalDraftChunks.Should().Be(5);
        stats.TotalAuditEvents.Should().Be(9);
        stats.FinalisationRatePercent.Should().Be(33);

        stats.SchemaStatistics.Should().HaveCount(2);
        var siteStat = stats.SchemaStatistics.First(s => s.SchemaName == "school_site");
        siteStat.DraftSubmissions.Should().Be(2);
        siteStat.FinalisedSubmissions.Should().Be(1);
        siteStat.TotalDraftChunks.Should().Be(5);

        var emptyStat = stats.SchemaStatistics.First(s => s.SchemaName == "empty_schema");
        emptyStat.TotalSubmissions.Should().Be(0);
    }

    [Fact]
    public async Task GetSubmissionDetail_For_Draft_With_Multiple_Chunks_Should_Merge_Validate_And_Build_NextSteps()
    {
        var infra = Substitute.For<ISubmissionInfrastructure>();
        var queryRepo = Substitute.For<ISubmissionQueryRepository>();
        var auditStore = Substitute.For<ISubmissionAuditStore>();
        var registry = Substitute.For<ISchemaRegistry>();
        var validator = new JsonSchemaNetValidator();

        var session = SubmissionSession.Create("edc_payload", 1);
        infra.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        infra.GetDraftItemsAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SubmissionDraftItem>>.Success(
            [
                new($"{session.SubmissionId}/drafts/01.json", t0, """{"urn":104521,"school_name":"Test School"}""", 40),
                new($"{session.SubmissionId}/drafts/02.json", t0.AddMinutes(5), """{"submitted_at_utc":"2026-11-20T10:00:00Z"}""", 42)
            ]));

        auditStore.ListAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SubmissionAuditEntry>>.Success([]));

        var resolved = new ResolvedSchemaBuilder().WithSchemaJson(JsonSchemaFixtures.ValidV1Schema).Build();
        registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        var sut = new GetSubmissionDetailUseCase(infra, queryRepo, auditStore, registry, validator);
        var detail = (await sut.ExecuteAsync(new GetSubmissionDetailRequest(session.SubmissionId))).ShouldSucceed();

        detail.DraftItems.Should().HaveCount(2);
        detail.MergedDraftPayloadJson.Should().Contain("104521");
        detail.MergedDraftPayloadJson.Should().Contain("Test School");
        detail.IsReadyToFinalise.Should().BeFalse();
        detail.PendingErrors.Should().NotBeEmpty();
        detail.NextSteps.Should().Contain(s => s.Contains("PUT /api/submissions/"));
    }

    [Fact]
    public void NextStepsBuilder_Should_Provide_Finalise_Instruction_When_Ready()
    {
        var id = Guid.NewGuid();
        var guidance = SubmissionNextStepsBuilder.Build(
            id,
            isFinalised: false,
            finalisedAtUtc: null,
            hasDraftPayload: true,
            draftCount: 2,
            pendingErrors: []);

        guidance.Summary.Should().Contain("Merged 2 timestamped draft submissions");
        guidance.NextSteps.Should().ContainSingle(s => s.Contains($"POST /api/submissions/{id}/finalise"));
    }

    [Fact]
    public async Task GetSubmissions_Should_Delegate_Filters_And_Collect_Available_Schema_Names()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var queryRepo = Substitute.For<ISubmissionQueryRepository>();

        registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success(
            [
                new SchemaMetadataBuilder().WithName("beta_schema").Build(),
                new SchemaMetadataBuilder().WithName("alpha_schema").Build()
            ]));

        queryRepo.ListSubmissionsAsync("alpha_schema", 1, "Draft", "1045", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SubmissionSummary>>.Success([]));

        var sut = new GetSubmissionsUseCase(registry, queryRepo);
        var res = (await sut.ExecuteAsync(new GetSubmissionsRequest("alpha_schema", 1, "Draft", "1045"))).ShouldSucceed();

        res.AvailableSchemaNames.Should().Equal("alpha_schema", "beta_schema");
        res.Status.Should().Be("Draft");
    }
}
