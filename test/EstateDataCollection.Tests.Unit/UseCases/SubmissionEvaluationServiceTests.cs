using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class SubmissionEvaluationServiceTests
{
    private readonly ISubmissionInfrastructure _infrastructure = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _schemaValidator = Substitute.For<ISchemaValidator>();

    private SubmissionEvaluationService CreateSut(IPayloadValidationService? validationService = null)
    {
        validationService ??= new PayloadValidationService(_schemaValidator, new JsonLogicValidator());
        return new SubmissionEvaluationService(_infrastructure, _registry, validationService);
    }

    [Fact]
    public async Task LoadDraftInventoryAsync_When_Infrastructure_Returns_Null_Should_Treat_As_Empty()
    {
        var submissionId = Guid.NewGuid();
        _infrastructure.GetDraftItemsAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns((Result<IReadOnlyList<SubmissionDraftItem>>)null!);

        using var scope = SubmissionExecutionScope.Start("CheckSubmissionStatus", submissionId);
        var sut = CreateSut();

        var inventory = await sut.LoadDraftInventoryAsync(submissionId, scope);

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
        var inventory = await sut.LoadDraftInventoryAsync(submissionId);

        inventory.Count.Should().Be(2);
        inventory.LastDraftAtUtc.Should().Be(t1);
    }

    [Fact]
    public async Task AssembleStoredDraftAsync_With_Multiple_Chunks_Should_Merge_And_Record_Process()
    {
        var submissionId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var inventory = new SubmissionDraftInventory(
        [
            new($"{submissionId}/drafts/01.json", t0, """{"urn":104521,"school_name":"Old"}""", 32),
            new($"{submissionId}/drafts/02.json", t0.AddMinutes(1), """{"school_name":"Updated","school_capacity":300}""", 47)
        ], 2, t0.AddMinutes(1));

        using var scope = SubmissionExecutionScope.Start("FinaliseSubmission", submissionId);
        var sut = CreateSut();

        var merged = (await sut.AssembleStoredDraftAsync(submissionId, inventory, scope)).ShouldSucceed();

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
        var submissionId = Guid.NewGuid();
        var inventory = new SubmissionDraftInventory([], 0, null);
        _infrastructure.GetDraftPayloadAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure(Error.NotFound(ErrorCodes.PayloadNotFound)));

        using var scope = SubmissionExecutionScope.Start("CheckSubmissionStatus", submissionId);
        var sut = CreateSut();

        var result = await sut.AssembleStoredDraftAsync(submissionId, inventory, scope);

        result.IsFailure.Should().BeTrue();
        sut.IsMissingDraftError(result.Error).Should().BeTrue();
        scope.Processes.Should().ContainSingle(p =>
            p.ProcessName == SubmissionProcessCatalog.GetDraftPayload.ProcessName &&
            p.Outcome == SubmissionProcessOutcome.Missing);
    }

    [Fact]
    public async Task TryAssembleDraftForDetailAsync_When_Chunk_Json_Is_Malformed_Should_Fall_Back_To_Last_Chunk()
    {
        var session = new SubmissionSessionBuilder().Build();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var inventory = new SubmissionDraftInventory(
        [
            new($"{session.SubmissionId}/drafts/01.json", t0, """{"urn":104521}""", 14),
            new($"{session.SubmissionId}/drafts/02.json", t0.AddMinutes(1), "{not-valid-json", 15)
        ], 2, t0.AddMinutes(1));

        var sut = CreateSut();
        var payload = await sut.TryAssembleDraftForDetailAsync(session, inventory);

        payload.Should().Be("{not-valid-json");
    }

    [Fact]
    public async Task TryAssembleDraftForDetailAsync_When_Finalised_And_No_Chunks_Should_Not_Load_Draft_Blob()
    {
        var session = new SubmissionSessionBuilder().WithUrn(104521).Finalised().Build();
        var inventory = new SubmissionDraftInventory([], 0, null);

        var sut = CreateSut();
        var payload = await sut.TryAssembleDraftForDetailAsync(session, inventory);

        payload.Should().BeNull();
        await _infrastructure.DidNotReceive().GetDraftPayloadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AssembleCandidateDraft_With_Existing_Chunks_Should_Merge_Incoming_Patch()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var inventory = new SubmissionDraftInventory(
        [
            new("sub/drafts/01.json", t0, """{"urn":104521,"school_name":"Alpha"}""", 32)
        ], 1, t0);

        using var scope = SubmissionExecutionScope.Start("UpdateSubmission", Guid.NewGuid());
        var sut = CreateSut();

        var candidate = sut.AssembleCandidateDraft(inventory, """{"school_address":"10 High St"}""", scope).ShouldSucceed();

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
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var inventory = new SubmissionDraftInventory(
        [
            new("sub/drafts/01.json", t0, """{"urn":104521}""", 14)
        ], 1, t0);

        var sut = CreateSut();
        var result = sut.AssembleCandidateDraft(inventory, "{invalid-json");

        result.ShouldFailWith("Json.Malformed");
    }

    [Fact]
    public async Task EvaluatePayloadAsync_When_SchemaValidator_Returns_JsonPath_Identifier_Error_Should_Deduplicate_With_JsonPointer()
    {
        var session = new SubmissionSessionBuilder().Build();
        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithUrnPointer("/urn"))
            .Build();
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _schemaValidator.ValidateAsync(SurveyPayloadFixtures.MissingUrnJson, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult(
                [new ValidationError("$.urn", "Missing URN", "RULE-URN")])));

        using var scope = SubmissionExecutionScope.Start("CheckSubmissionStatus", session.SubmissionId);
        var sut = CreateSut();

        var evaluation = (await sut.EvaluatePayloadAsync(
            SurveyPayloadFixtures.MissingUrnJson,
            session,
            isDraftUpdate: false,
            scope)).ShouldSucceed();

        evaluation.ValidationErrors.Should().ContainSingle(e => e.PropertyPath == "$.urn");
        evaluation.IdentifierEvaluation.IsValid.Should().BeFalse();
        scope.Processes.Select(p => p.ProcessName)
            .Should().Contain(["SchemaResolution", "JsonSchemaValidator", "IdentifierExtractor"]);
    }

    [Fact]
    public async Task EvaluatePayloadAsync_When_Mock_ValidationService_Omits_IdentifierEvaluation_Should_Populate_Trace_And_AuditIdentifier()
    {
        var session = new SubmissionSessionBuilder().Build();
        var resolved = new ResolvedSchemaBuilder().Build();
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        var mockValidation = Substitute.For<IPayloadValidationService>();
        mockValidation.ValidatePayloadWithTraceAsync(
                SurveyPayloadFixtures.ValidPayloadJson,
                resolved,
                false,
                Arg.Any<CancellationToken>())
            .Returns(Result<PayloadValidationReport>.Success(new PayloadValidationReport(
                [],
                [new SubmissionProcessExecutionBuilder().For(SubmissionProcessCatalog.JsonSchemaValidator).Build()])));

        using var scope = SubmissionExecutionScope.Start("FinaliseSubmission", session.SubmissionId);
        var sut = CreateSut(mockValidation);

        var evaluation = (await sut.EvaluatePayloadAsync(
            SurveyPayloadFixtures.ValidPayloadJson,
            session,
            isDraftUpdate: false,
            scope)).ShouldSucceed();

        evaluation.ValidationErrors.Should().BeEmpty();
        evaluation.IdentifierEvaluation.IsValid.Should().BeTrue();
        evaluation.AuditIdentifier?.Urn.Should().Be(104521);
        scope.Processes.Select(p => p.ProcessName)
            .Should().Contain(["SchemaResolution", "JsonSchemaValidator", "IdentifierExtractor"]);
    }
}
