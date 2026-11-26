using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class DraftAssemblyEvaluationTests
{
    private readonly ISubmissionInfrastructure _infrastructure = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _schemaValidator = Substitute.For<ISchemaValidator>();

    private SubmissionEvaluationService CreateSut()
    {
        var validationService = new PayloadValidationService(_schemaValidator, new QualityRuleCoordinator());
        return new SubmissionEvaluationService(_infrastructure, _registry, validationService);
    }

    [Fact]
    public async Task LoadDraftInventoryAsync_When_Infrastructure_Returns_Null_Should_Treat_As_Empty()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        _infrastructure.GetDraftItemsAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns((Result<IReadOnlyList<SubmissionDraftItem>>)null!);

        using var scope = SubmissionExecutionScope.Start("CheckSubmissionStatus", submissionId);
        var sut = CreateSut();

        // Act
        var inventory = await sut.LoadDraftInventoryAsync(submissionId);

        // Assert
        inventory.Count.Should().Be(0);
        inventory.Items.Should().BeEmpty();
        inventory.LastDraftAtUtc.Should().BeNull();
        scope.Processes.Should().ContainSingle(p =>
            p.ProcessName == SubmissionProcessCatalog.GetDraftItems.ProcessName &&
            p.Outcome == SubmissionProcessOutcome.Succeeded &&
            p.ItemCount == 0);
    }

    [Fact]
    public async Task LoadDraftInventoryAsync_Should_Compute_Latest_Timestamp_From_Chunks()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-20);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        _infrastructure.GetDraftItemsAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SubmissionDraftItem>>.Success(
            [
                new($"{submissionId}/drafts/01.json", t0, """{"urn":104521}""", 14),
                new($"{submissionId}/drafts/02.json", t1, """{"school_name":"Alpha"}""", 23)
            ]));

        var sut = CreateSut();

        // Act
        var inventory = await sut.LoadDraftInventoryAsync(submissionId);

        // Assert
        inventory.Count.Should().Be(2);
        inventory.LastDraftAtUtc.Should().Be(t1);
    }

    [Fact]
    public async Task AssembleStoredDraftAsync_With_Multiple_Chunks_Should_Merge_And_Record_Process()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var inventory = new SubmissionDraftInventory(
        [
            new($"{submissionId}/drafts/01.json", t0, """{"urn":104521,"school_name":"Old"}""", 32),
            new($"{submissionId}/drafts/02.json", t0.AddMinutes(1), """{"school_name":"Updated","school_capacity":300}""", 47)
        ], 2, t0.AddMinutes(1));

        using var scope = SubmissionExecutionScope.Start("FinaliseSubmission", submissionId);
        var sut = CreateSut();

        // Act
        var merged = (await sut.AssembleStoredDraftAsync(submissionId, inventory)).ShouldSucceed();

        // Assert
        merged.Should().Contain("104521");
        merged.Should().Contain("Updated");
        merged.Should().Contain("300");
        scope.Processes.Should().ContainSingle(p =>
            p.ProcessName == SubmissionProcessCatalog.DraftPayloadMerger.ProcessName &&
            p.Outcome == SubmissionProcessOutcome.Succeeded &&
            p.ItemCount == 2);
        await _infrastructure.DidNotReceive().GetDraftPayloadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssembleStoredDraftAsync_With_No_Chunks_And_Missing_Payload_Should_Record_Missing_Outcome()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var inventory = new SubmissionDraftInventory([], 0, null);
        _infrastructure.GetDraftPayloadAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure(Error.NotFound(ErrorCodes.PayloadNotFound)));

        using var scope = SubmissionExecutionScope.Start("CheckSubmissionStatus", submissionId);
        var sut = CreateSut();

        // Act
        var result = await sut.AssembleStoredDraftAsync(submissionId, inventory);

        // Assert
        result.IsFailure.Should().BeTrue();
        sut.IsMissingDraftError(result.Error).Should().BeTrue();
        scope.Processes.Should().ContainSingle(p =>
            p.ProcessName == SubmissionProcessCatalog.GetDraftPayload.ProcessName &&
            p.Outcome == SubmissionProcessOutcome.Missing);
    }

    [Fact]
    public async Task TryAssembleDraftForDetailAsync_When_Chunk_Json_Is_Malformed_Should_Fall_Back_To_Last_Chunk()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().Build();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var inventory = new SubmissionDraftInventory(
        [
            new($"{session.SubmissionId}/drafts/01.json", t0, """{"urn":104521}""", 14),
            new($"{session.SubmissionId}/drafts/02.json", t0.AddMinutes(1), "{not-valid-json", 15)
        ], 2, t0.AddMinutes(1));

        var sut = CreateSut();

        // Act
        var payload = await sut.TryAssembleDraftForDetailAsync(session, inventory);

        // Assert
        payload.Should().Be("{not-valid-json");
    }

    [Fact]
    public async Task TryAssembleDraftForDetailAsync_When_Finalised_And_No_Chunks_Should_Not_Load_Draft_Blob()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().WithIdentifier("URN", "104521").Finalised().Build();
        var inventory = new SubmissionDraftInventory([], 0, null);
        var sut = CreateSut();

        // Act
        var payload = await sut.TryAssembleDraftForDetailAsync(session, inventory);

        // Assert
        payload.Should().BeNull();
        await _infrastructure.DidNotReceive().GetDraftPayloadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AssembleCandidateDraft_With_Existing_Chunks_Should_Merge_Incoming_Patch()
    {
        // Arrange
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var inventory = new SubmissionDraftInventory(
        [
            new("sub/drafts/01.json", t0, """{"urn":104521,"school_name":"Alpha"}""", 32)
        ], 1, t0);

        using var scope = SubmissionExecutionScope.Start("UpdateSubmission", Guid.NewGuid());
        var sut = CreateSut();

        // Act
        var candidate = sut.AssembleCandidateDraft(inventory, """{"school_address":"10 High St"}""").ShouldSucceed();

        // Assert
        candidate.DraftCountAfterSave.Should().Be(2);
        candidate.MergedPayloadJson.Should().Contain("104521");
        candidate.MergedPayloadJson.Should().Contain("Alpha");
        candidate.MergedPayloadJson.Should().Contain("10 High St");
        scope.Processes.Should().ContainSingle(p =>
            p.ProcessName == SubmissionProcessCatalog.DraftPayloadMerger.ProcessName &&
            p.ItemCount == 2);
    }

    [Fact]
    public void AssembleCandidateDraft_When_Incoming_Patch_Is_Malformed_Should_Fail_With_JsonMalformed()
    {
        // Arrange
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var inventory = new SubmissionDraftInventory(
        [
            new("sub/drafts/01.json", t0, """{"urn":104521}""", 14)
        ], 1, t0);

        var sut = CreateSut();

        // Act
        var result = sut.AssembleCandidateDraft(inventory, "{invalid-json");

        // Assert
        result.ShouldFailWith("Json.Malformed");
    }
}
