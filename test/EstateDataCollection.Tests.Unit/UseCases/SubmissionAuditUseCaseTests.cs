using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
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

public sealed class SubmissionAuditUseCaseTests
{
    private readonly ISubmissionInfrastructure _infrastructure = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _validator = Substitute.For<ISchemaValidator>();
    private readonly IPayloadValidationService _validationService = Substitute.For<IPayloadValidationService>();
    private readonly ISubmissionAuditStore _auditStore = Substitute.For<ISubmissionAuditStore>();

    public SubmissionAuditUseCaseTests()
    {
        _auditStore.AppendAsync(Arg.Any<SubmissionAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("sub/audit/entry.json"));
    }

    [Fact]
    public async Task CreateSubmission_Should_Record_Audit_Entry_With_SchemaResolution_And_CreateSession_Processes()
    {
        var metadata = new SchemaMetadataBuilder().Build();
        var resolved = new ResolvedSchema(metadata, JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema));

        _registry.ResolveActiveSchemaAsync(metadata.Name, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _infrastructure.CreateSessionAsync(Arg.Any<SubmissionSession>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<Guid>.Success(ci.Arg<SubmissionSession>().SubmissionId));

        SubmissionAuditEntry? recorded = null;
        await _auditStore.AppendAsync(Arg.Do<SubmissionAuditEntry>(e => recorded = e), Arg.Any<CancellationToken>());

        var sut = new CreateSubmissionUseCase(_registry, _infrastructure, auditStore: _auditStore);
        var result = (await sut.ExecuteAsync(new CreateSubmissionRequest(metadata.Name, null))).ShouldSucceed();

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
        var metadata = new SchemaMetadataBuilder().Build();
        var resolved = new ResolvedSchema(metadata, JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema));

        _registry.ResolveActiveSchemaAsync(metadata.Name, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _infrastructure.CreateSessionAsync(Arg.Any<SubmissionSession>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<Guid>.Success(ci.Arg<SubmissionSession>().SubmissionId));
        _auditStore.AppendAsync(Arg.Any<SubmissionAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<string>>>(_ => throw new InvalidOperationException("Transient audit store outage"));

        var sut = new CreateSubmissionUseCase(_registry, _infrastructure, auditStore: _auditStore);
        var result = await sut.ExecuteAsync(new CreateSubmissionRequest(metadata.Name, null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSubmission_With_Validation_Errors_Should_Audit_SavedWithPendingErrors_And_Processes()
    {
        var session = new SubmissionSessionBuilder().Build();
        var metadata = new SchemaMetadataBuilder().WithName(session.SchemaName).Build();
        var resolved = new ResolvedSchema(metadata, JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema));

        _infrastructure.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _infrastructure.SaveDraftPayloadAsync(session.SubmissionId, null, SurveyPayloadFixtures.MissingUrnJson, Arg.Any<CancellationToken>())
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

        var sut = new UpdateSubmissionUseCase(
            _infrastructure,
            _registry,
            _validator,
            validationService: _validationService,
            auditStore: _auditStore);

        var result = (await sut.ExecuteAsync(new UpdateSubmissionRequest(session.SubmissionId, SurveyPayloadFixtures.MissingUrnJson)))
            .ShouldSucceed();

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
    public async Task FinaliseSubmission_When_Already_Finalised_Should_Audit_Rejected_403()
    {
        var session = new SubmissionSessionBuilder().WithUrn(104521).Finalised().Build();
        _infrastructure.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));

        SubmissionAuditEntry? recorded = null;
        await _auditStore.AppendAsync(Arg.Do<SubmissionAuditEntry>(e => recorded = e), Arg.Any<CancellationToken>());

        var sut = new FinaliseSubmissionUseCase(
            _infrastructure,
            _registry,
            _validator,
            auditStore: _auditStore);

        var result = await sut.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId));

        result.ShouldFailWith("Submission.AlreadyFinalised");
        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be("FinaliseSubmission");
        recorded.Outcome.Status.Should().Be("Rejected");
        recorded.Outcome.HttpStatus.Should().Be(403);
        recorded.Outcome.ErrorCode.Should().Be("Submission.AlreadyFinalised");
        recorded.Identifier?.Urn.Should().Be(104521);
    }
}
