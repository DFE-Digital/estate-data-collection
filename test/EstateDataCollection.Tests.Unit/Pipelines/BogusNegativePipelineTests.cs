using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fuzzing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Pipelines;

public sealed class BogusNegativePipelineTests
{
    public static IEnumerable<object[]> ValidBaselineCases()
    {
        int[] seeds = [101, 202, 303];
        foreach (var profile in Enum.GetValues<FuzzSchemaProfile>())
        {
            foreach (var seed in seeds)
            {
                yield return [profile, seed];
            }
        }
    }

    public static IEnumerable<object[]> NegativePipelineCases()
    {
        foreach (var negativeCase in BogusNegativePayloadSuite.GenerateAllNegativeCases(101, 202))
        {
            yield return [negativeCase];
        }
    }

    public static IEnumerable<object[]> MergePatchPoisonCases()
    {
        yield return
        [
            FuzzSchemaProfile.EdcSchoolSite,
            """{"survey":{"updated_at":"2020-01-01T00:00:00.0000000Z"}}"""
        ];
        yield return
        [
            FuzzSchemaProfile.EdcSchoolSite,
            """{"school":{"urn":"NOT-SIX-DIGITS"}}"""
        ];
        yield return
        [
            FuzzSchemaProfile.EdcSurveyElements,
            """{"elements":[{"id":"BAD-ID","NRM3_code":"2.1","description":"Roof","asset_id":"B1","asset_type":"block"}]}"""
        ];
        yield return
        [
            FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers,
            """{"answers":[{"id":"ANS-1","question_id":"Q1","asset_id":"B1","asset_type":"invalid_enum","value":"x"}]}"""
        ];
        yield return
        [
            FuzzSchemaProfile.CustomLaEstabCel,
            """{"summary":{"total_checks":5,"open_issues":99}}"""
        ];
    }

    [Theory]
    [MemberData(nameof(ValidBaselineCases))]
    public async Task Valid_Bogus_Baselines_Across_All_Schema_Profiles_Should_Complete_Pipeline(
        FuzzSchemaProfile profile,
        int seed)
    {
        var harness = CreatePipelineHarness(profile);
        var validJson = BogusNegativePayloadSuite.GenerateValidJson(profile, seed);

        var update = (await harness.UpdateUseCase.ExecuteAsync(
            new UpdateSubmissionRequest(harness.SubmissionId, validJson))).ShouldSucceed();

        update.IsSuccessful.Should().BeTrue($"baseline {profile} seed {seed} should be valid: {string.Join("; ", update.PendingErrors.Select(e => e.Message))}");
        update.PendingErrors.Should().BeEmpty();

        var status = (await harness.StatusUseCase.ExecuteAsync(
            new GetSubmissionStatusRequest(harness.SubmissionId))).ShouldSucceed();

        status.IsReadyToFinalise.Should().BeTrue();
        status.PendingErrors.Should().BeEmpty();

        var finalise = (await harness.FinaliseUseCase.ExecuteAsync(
            new FinaliseSubmissionRequest(harness.SubmissionId))).ShouldSucceed();

        finalise.Status.Should().Be("Finalised");
        finalise.Identifier.Should().NotBeNullOrWhiteSpace();
        harness.Infrastructure.PromotionCount.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(NegativePipelineCases))]
    public async Task Bogus_Negative_Payloads_Should_Fail_Cleanly_Through_Whole_Submission_Pipeline(
        NegativePayloadCase negativeCase)
    {
        var harness = CreatePipelineHarness(negativeCase.Profile);

        var updateResult = await harness.UpdateUseCase.ExecuteAsync(
            new UpdateSubmissionRequest(harness.SubmissionId, negativeCase.PayloadJson));

        if (negativeCase.IsMalformedJson)
        {
            updateResult.IsFailure.Should().BeTrue($"malformed payload ({negativeCase.Description}) must fail Update");
            updateResult.Error.Code.Should().BeOneOf("Json.Malformed", "Json.EmptyPayload");
            harness.Infrastructure.SavedDraftChunks.Should().BeEmpty();

            var finaliseEmpty = await harness.FinaliseUseCase.ExecuteAsync(
                new FinaliseSubmissionRequest(harness.SubmissionId));
            finaliseEmpty.IsFailure.Should().BeTrue();
            harness.Infrastructure.PromotionCount.Should().Be(0);
            return;
        }

        var update = updateResult.ShouldSucceed();
        update.IsSuccessful.Should().BeFalse($"negative case {negativeCase.Description} must not validate cleanly");
        update.PendingErrors.Should().NotBeEmpty($"negative case {negativeCase.Description} must emit pending errors");

        var status = (await harness.StatusUseCase.ExecuteAsync(
            new GetSubmissionStatusRequest(harness.SubmissionId))).ShouldSucceed();
        status.IsReadyToFinalise.Should().BeFalse($"negative case {negativeCase.Description} must not be ready to finalise");
        status.PendingErrors.Should().NotBeEmpty();

        var finalise = await harness.FinaliseUseCase.ExecuteAsync(
            new FinaliseSubmissionRequest(harness.SubmissionId));
        finalise.IsFailure.Should().BeTrue($"negative case {negativeCase.Description} must be rejected at finalisation");
        finalise.Error.Type.Should().Be(ErrorType.Validation);
        finalise.Error.Code.Should().BeOneOf("Schema.ContractViolation", "Payload.InvalidIdentifier", "Json.EmptyPayload");
        harness.Infrastructure.PromotionCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(MergePatchPoisonCases))]
    public async Task Merge_Patch_With_Bogus_Regression_Should_Block_Finalise_Until_Repaired(
        FuzzSchemaProfile profile,
        string poisoningPatchJson)
    {
        var harness = CreatePipelineHarness(profile);
        var validJson = BogusNegativePayloadSuite.GenerateValidJson(profile, 404);

        // Chunk 1: valid baseline passes
        var firstUpdate = (await harness.UpdateUseCase.ExecuteAsync(
            new UpdateSubmissionRequest(harness.SubmissionId, validJson))).ShouldSucceed();
        firstUpdate.IsSuccessful.Should().BeTrue();

        // Chunk 2: poisoning patch regresses schema or quality rule
        var poisonedUpdate = (await harness.UpdateUseCase.ExecuteAsync(
            new UpdateSubmissionRequest(harness.SubmissionId, poisoningPatchJson))).ShouldSucceed();
        poisonedUpdate.IsSuccessful.Should().BeFalse();
        poisonedUpdate.PendingErrors.Should().NotBeEmpty();
        poisonedUpdate.DraftCount.Should().Be(2);

        var blockedFinalise = await harness.FinaliseUseCase.ExecuteAsync(
            new FinaliseSubmissionRequest(harness.SubmissionId));
        blockedFinalise.IsFailure.Should().BeTrue();
        blockedFinalise.Error.Code.Should().Be("Schema.ContractViolation");
        harness.Infrastructure.PromotionCount.Should().Be(0);

        // Chunk 3: re-applying valid baseline repairs merged document
        var recoveryUpdate = (await harness.UpdateUseCase.ExecuteAsync(
            new UpdateSubmissionRequest(harness.SubmissionId, validJson))).ShouldSucceed();
        recoveryUpdate.IsSuccessful.Should().BeTrue();

        var repairedFinalise = (await harness.FinaliseUseCase.ExecuteAsync(
            new FinaliseSubmissionRequest(harness.SubmissionId))).ShouldSucceed();
        repairedFinalise.Status.Should().Be("Finalised");
        harness.Infrastructure.PromotionCount.Should().Be(1);
    }

    private static PipelineHarness CreatePipelineHarness(FuzzSchemaProfile profile)
    {
        var contract = BogusNegativePayloadSuite.GetContract(profile);
        var rules = string.IsNullOrWhiteSpace(contract.RulesJson)
            ? []
            : JsonSerializer.Deserialize<List<BusinessRule>>(
                contract.RulesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var resolvedSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(contract.SchemaJson)
            .ConfigureMetadata(m => m
                .WithName(contract.SchemaNamePrefix)
                .WithVersion(1)
                .WithIdentifierConfig(contract.IdentifierConfig))
            .WithRules(rules)
            .Build();

        var session = new SubmissionSessionBuilder()
            .WithSchema(contract.SchemaNamePrefix, 1)
            .Build();

        var infrastructure = new InMemorySubmissionInfrastructure(session);
        var registry = Substitute.For<ISchemaRegistry>();
        registry.GetExplicitSchemaAsync(contract.SchemaNamePrefix, 1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<Application.Models.Contracts.ResolvedSchema>.Success(resolvedSchema)));

        var validationOptions = Options.Create(new ValidationOptions
        {
            RunQualityRulesOnDraftUpdate = true,
            ValidateIdentifierOnDraftUpdate = true,
            RequireFormatValidation = true,
            UnknownEngineBehavior = UnknownEngineBehavior.Fail
        });

        var schemaValidator = new JsonSchemaNetValidator(options: validationOptions);
        var logicValidator = new JsonLogicValidator([], options: validationOptions);
        var validationService = new PayloadValidationService(schemaValidator, logicValidator, validationOptions);
        var evaluationService = new SubmissionEvaluationService(infrastructure, registry, validationService, validationOptions);

        return new PipelineHarness(
            session.SubmissionId,
            infrastructure,
            new UpdateSubmissionUseCase(infrastructure, evaluationService),
            new GetSubmissionStatusUseCase(infrastructure, evaluationService),
            new FinaliseSubmissionUseCase(infrastructure, evaluationService));
    }

    private sealed record PipelineHarness(
        Guid SubmissionId,
        InMemorySubmissionInfrastructure Infrastructure,
        UpdateSubmissionUseCase UpdateUseCase,
        GetSubmissionStatusUseCase StatusUseCase,
        FinaliseSubmissionUseCase FinaliseUseCase);

    private sealed class InMemorySubmissionInfrastructure(SubmissionSession session) : ISubmissionInfrastructure
    {
        private readonly List<SubmissionDraftItem> _chunks = [];
        private int _sequence;

        public IReadOnlyList<SubmissionDraftItem> SavedDraftChunks => _chunks;
        public int PromotionCount { get; private set; }

        public Task<Result<Guid>> CreateSessionAsync(SubmissionSession newSession, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<Guid>.Success(newSession.SubmissionId));

        public Task<Result<SubmissionSession>> GetSessionAsync(Guid submissionId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<SubmissionSession>.Success(session));

        public Task<Result> SaveDraftPayloadAsync(Guid submissionId, string rawPayloadJson, CancellationToken cancellationToken = default)
        {
            _sequence++;
            var ts = DateTimeOffset.UtcNow.AddSeconds(_sequence);
            _chunks.Add(new SubmissionDraftItem(
                $"{submissionId}/drafts/{_sequence:D4}.json",
                ts,
                rawPayloadJson,
                rawPayloadJson.Length));
            return Task.FromResult(Result.Success());
        }

        public Task<Result<string>> GetDraftPayloadAsync(Guid submissionId, CancellationToken cancellationToken = default)
            => _chunks.Count == 0
                ? Task.FromResult(Result<string>.Failure(Error.NotFound(ErrorCodes.PayloadNotFound)))
                : Task.FromResult(Result<string>.Success(_chunks[^1].PayloadJson));

        public Task<Result<IReadOnlyList<SubmissionDraftItem>>> GetDraftItemsAsync(Guid submissionId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<SubmissionDraftItem>>.Success(_chunks.ToArray()));

        public Task<Result<string>> PromoteToProcessedAsync(
            Guid submissionId,
            string? identifierType,
            string? identifierValue,
            string schemaName,
            int schemaVersion,
            CancellationToken cancellationToken = default)
        {
            PromotionCount++;
            session.AssignIdentifier(identifierType, identifierValue);
            session.Finalise();
            return Task.FromResult(Result<string>.Success($"https://memory.blob.local/processed/schema={schemaName}/v={schemaVersion}/{submissionId}.json"));
        }
    }
}
