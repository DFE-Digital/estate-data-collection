using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Json.Schema;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class SubmissionAuditCreateAndUpdateTests
{
    private readonly ISubmissionInfrastructure _infrastructure = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly IPayloadValidationService _validationService = Substitute.For<IPayloadValidationService>();
    private readonly ISubmissionAuditStore _auditStore = Substitute.For<ISubmissionAuditStore>();

    public SubmissionAuditCreateAndUpdateTests()
    {
        _auditStore.AppendAsync(Arg.Any<SubmissionAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("sub/audit/entry.json"));
    }

    private IUseCase<TResult, TRequest> Audited<TResult, TRequest>(IUseCase<TResult, TRequest> inner)
        => new SubmissionUseCaseAuditDecorator<TResult, TRequest>(inner, _auditStore);

    [Fact]
    public async Task CreateSubmission_Should_Record_Audit_Entry_With_SchemaResolution_And_CreateSession_Processes()
    {
        // Arrange
        var metadata = new SchemaMetadataBuilder().Build();
        var resolved = new ResolvedSchema(metadata, JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema));

        _registry.ResolveActiveSchemaAsync(metadata.Name, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _infrastructure.CreateSessionAsync(Arg.Any<SubmissionSession>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<Guid>.Success(ci.Arg<SubmissionSession>().SubmissionId));

        SubmissionAuditEntry? recorded = null;
        await _auditStore.AppendAsync(Arg.Do<SubmissionAuditEntry>(e => recorded = e), Arg.Any<CancellationToken>());

        var sut = Audited(new CreateSubmissionUseCase(_registry, _infrastructure));
        var request = new CreateSubmissionRequest(metadata.Name, null);

        // Act
        var result = (await sut.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        await _auditStore.Received(1).AppendAsync(Arg.Any<SubmissionAuditEntry>(), Arg.Any<CancellationToken>());
        recorded.Should().NotBeNull();
        recorded!.SubmissionId.Should().Be(result.SubmissionId);
        recorded.Action.Should().Be("CreateSubmission");
        recorded.Outcome.Status.Should().Be("Succeeded");
        recorded.Outcome.HttpStatus.Should().Be(201);
        recorded.ProcessesConsumed.Select(p => p.ProcessName)
            .Should().Contain(["SchemaResolution", "BlobStorage:CreateSession"]);
    }

    [Fact]
    public async Task CreateSubmission_Should_Still_Succeed_If_AuditStore_Throws()
    {
        // Arrange
        var metadata = new SchemaMetadataBuilder().Build();
        var resolved = new ResolvedSchema(metadata, JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema));

        _registry.ResolveActiveSchemaAsync(metadata.Name, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _infrastructure.CreateSessionAsync(Arg.Any<SubmissionSession>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<Guid>.Success(ci.Arg<SubmissionSession>().SubmissionId));
        _auditStore.AppendAsync(Arg.Any<SubmissionAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<string>>>(_ => throw new InvalidOperationException("Transient audit store outage"));

        var sut = Audited(new CreateSubmissionUseCase(_registry, _infrastructure));
        var request = new CreateSubmissionRequest(metadata.Name, null);

        // Act
        var result = await sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSubmission_With_Validation_Errors_Should_Audit_SavedWithPendingErrors_And_Processes()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().Build();
        var metadata = new SchemaMetadataBuilder().WithName(session.SchemaName).Build();
        var resolved = new ResolvedSchema(metadata, JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema));

        _infrastructure.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _infrastructure.SaveDraftPayloadAsync(session.SubmissionId, SurveyPayloadFixtures.MissingUrnJson, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        List<ValidationError> errs = [new ValidationError("$.urn", "Missing URN", "RULE-URN")];
        List<SubmissionProcessExecution> processes =
        [
            new SubmissionProcessExecutionBuilder()
                .For(SubmissionProcessCatalog.JsonSchemaValidator)
                .WithOutcome(SubmissionProcessOutcome.ValidationErrorsFound)
                .WithDurationMs(2)
                .WithItemCount(1)
                .Build()
        ];
        _validationService.ValidatePayloadWithTraceAsync(
                SurveyPayloadFixtures.MissingUrnJson,
                resolved,
                true,
                Arg.Any<CancellationToken>())
            .Returns(Result<PayloadValidationReport>.Success(new PayloadValidationReport(errs, processes)));

        SubmissionAuditEntry? recorded = null;
        await _auditStore.AppendAsync(Arg.Do<SubmissionAuditEntry>(e => recorded = e), Arg.Any<CancellationToken>());

        var evaluationService = new SubmissionEvaluationService(_infrastructure, _registry, _validationService);
        var sut = Audited(new UpdateSubmissionUseCase(_infrastructure, evaluationService));
        var request = new UpdateSubmissionRequest(session.SubmissionId, SurveyPayloadFixtures.MissingUrnJson);

        // Act
        var result = (await sut.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be("UpdateSubmission");
        recorded.Outcome.Status.Should().Be("SavedWithPendingErrors");
        recorded.Outcome.HttpStatus.Should().Be(200);
        recorded.Outcome.ValidationErrorCount.Should().Be(1);
        recorded.ProcessesConsumed.Should().Contain(p => p.ProcessName == "JsonSchemaValidator" && p.Engine == "json-schema");
        recorded.ProcessesConsumed.Should().Contain(p => p.ProcessName == "BlobStorage:SaveDraftPayload");
    }

    [Fact]
    public async Task UpdateSubmission_When_SchemaResolution_Fails_Should_Record_ErrorCode_And_Errors_On_Failed_Process()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().Build();
        _infrastructure.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Failure(Error.NotFound("Schema.NotFound", "Pinned schema version was not found.")));

        SubmissionAuditEntry? recorded = null;
        await _auditStore.AppendAsync(Arg.Do<SubmissionAuditEntry>(e => recorded = e), Arg.Any<CancellationToken>());

        var evaluationService = new SubmissionEvaluationService(_infrastructure, _registry, _validationService);
        var sut = Audited(new UpdateSubmissionUseCase(_infrastructure, evaluationService));
        var request = new UpdateSubmissionRequest(session.SubmissionId, "{}");

        // Act
        var result = await sut.ExecuteAsync(request);

        // Assert
        result.ShouldFailWith("Schema.NotFound");
        recorded.Should().NotBeNull();
        var step = recorded!.ProcessesConsumed.Single(p => p.ProcessName == "SchemaResolution");
        step.Outcome.Should().Be(SubmissionProcessOutcome.Failed);
        step.ErrorCode.Should().Be("Schema.NotFound");
        step.Errors.Should().ContainSingle();
        step.Errors![0].Message.Should().Contain("Pinned schema version was not found.");
        step.Errors[0].RuleId.Should().Be("Schema.NotFound");
    }
}
