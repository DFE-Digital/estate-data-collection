using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Json.Schema;
using Microsoft.Extensions.DependencyInjection;
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

    private IUseCase<TResult, TRequest> Audited<TResult, TRequest>(IUseCase<TResult, TRequest> inner)
    {
        return new SubmissionUseCaseAuditDecorator<TResult, TRequest>(inner, _auditStore);
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

        var sut = Audited(new CreateSubmissionUseCase(_registry, _infrastructure));
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

        var sut = Audited(new CreateSubmissionUseCase(_registry, _infrastructure));
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

        var evaluationService = new SubmissionEvaluationService(_infrastructure, _registry, _validationService);
        var sut = Audited(new UpdateSubmissionUseCase(_infrastructure, evaluationService));

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

        var validationService = new PayloadValidationService(_validator, new JsonLogicValidator());
        var evaluationService = new SubmissionEvaluationService(_infrastructure, _registry, validationService);
        var sut = Audited(new FinaliseSubmissionUseCase(_infrastructure, evaluationService));

        var result = await sut.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId));

        result.ShouldFailWith("Submission.AlreadyFinalised");
        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be("FinaliseSubmission");
        recorded.Outcome.Status.Should().Be("Rejected");
        recorded.Outcome.HttpStatus.Should().Be(403);
        recorded.Outcome.ErrorCode.Should().Be("Submission.AlreadyFinalised");
        recorded.Identifier?.Urn.Should().Be(104521);
    }

    [Fact]
    public async Task FinaliseSubmission_Should_Record_Per_Process_Errors_On_ProcessesConsumed()
    {
        var session = new SubmissionSessionBuilder().WithUrn(104521).Build();
        var rule = new BusinessRule
        {
            Id = "RULE-CAP",
            Path = "school_capacity",
            Description = "Capacity must be positive",
            Severity = "ERROR",
            Implementation = JsonNode.Parse("""{">":[{"var":"value"},0]}""")
        };
        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName(session.SchemaName).WithUrnPointer("/urn", required: true))
            .WithSchemaJson(JsonSchemaFixtures.ValidV1Schema)
            .WithRules([rule])
            .Build();
        const string payloadJson = """{"urn":104521,"school_name":"Oak Academy","school_address":"1 High St","school_capacity":-5}""";

        _infrastructure.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _infrastructure.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(payloadJson));
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        SubmissionAuditEntry? recorded = null;
        await _auditStore.AppendAsync(Arg.Do<SubmissionAuditEntry>(e => recorded = e), Arg.Any<CancellationToken>());

        var validationService = new PayloadValidationService(new JsonSchemaNetValidator(), new JsonLogicValidator());
        var evaluationService = new SubmissionEvaluationService(_infrastructure, _registry, validationService);
        var sut = Audited(new FinaliseSubmissionUseCase(_infrastructure, evaluationService));

        var result = await sut.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId));

        result.IsFailure.Should().BeTrue();
        recorded.Should().NotBeNull();
        recorded!.Outcome.Status.Should().Be("Rejected");

        var schemaStep = recorded.ProcessesConsumed.Single(p => p.ProcessName == "JsonSchemaValidator");
        schemaStep.Outcome.Should().Be(SubmissionProcessOutcome.Succeeded);
        schemaStep.ErrorCode.Should().BeNull();
        schemaStep.Errors.Should().BeNull();

        var ruleStep = recorded.ProcessesConsumed.Single(p => p.ProcessName == "QualityRulesValidator");
        ruleStep.Outcome.Should().Be(SubmissionProcessOutcome.ValidationErrorsFound);
        ruleStep.Errors.Should().ContainSingle();
        ruleStep.Errors![0].PropertyPath.Should().Contain("school_capacity");
        ruleStep.Errors[0].RuleId.Should().Be("RULE-CAP");
    }

    [Fact]
    public async Task UpdateSubmission_When_SchemaResolution_Fails_Should_Record_ErrorCode_And_Errors_On_Failed_Process()
    {
        var session = new SubmissionSessionBuilder().Build();
        _infrastructure.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Failure(Error.NotFound("Schema.NotFound", "Pinned schema version was not found.")));

        SubmissionAuditEntry? recorded = null;
        await _auditStore.AppendAsync(Arg.Do<SubmissionAuditEntry>(e => recorded = e), Arg.Any<CancellationToken>());

        var evaluationService = new SubmissionEvaluationService(_infrastructure, _registry, _validationService);
        var sut = Audited(new UpdateSubmissionUseCase(_infrastructure, evaluationService));
        var result = await sut.ExecuteAsync(new UpdateSubmissionRequest(session.SubmissionId, "{}"));

        result.ShouldFailWith("Schema.NotFound");
        recorded.Should().NotBeNull();
        var step = recorded!.ProcessesConsumed.Single(p => p.ProcessName == "SchemaResolution");
        step.Outcome.Should().Be(SubmissionProcessOutcome.Failed);
        step.ErrorCode.Should().Be("Schema.NotFound");
        step.Errors.Should().ContainSingle();
        step.Errors![0].Message.Should().Contain("Pinned schema version was not found.");
        step.Errors[0].RuleId.Should().Be("Schema.NotFound");
    }

    [Fact]
    public async Task GetSubmissionStatus_When_Resolved_Via_Scrutor_DI_Should_Decorate_And_Audit_CheckSubmission()
    {
        var session = new SubmissionSessionBuilder().Build();
        _infrastructure.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _infrastructure.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure(Error.NotFound(ErrorCodes.PayloadNotFound)));

        SubmissionAuditEntry? recorded = null;
        await _auditStore.AppendAsync(Arg.Do<SubmissionAuditEntry>(e => recorded = e), Arg.Any<CancellationToken>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddScoped(_ => _infrastructure);
        services.AddScoped(_ => _registry);
        services.AddScoped(_ => _validator);
        services.AddScoped(_ => Substitute.For<IBusinessLogicValidator>());
        services.AddScoped(_ => Substitute.For<ISchemaVerificationService>());
        services.AddScoped(_ => _auditStore);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var resolvedUseCase = scope.ServiceProvider.GetRequiredService<IUseCase<GetSubmissionStatusResult, GetSubmissionStatusRequest>>();
        resolvedUseCase.Should().BeOfType<SubmissionUseCaseAuditDecorator<GetSubmissionStatusResult, GetSubmissionStatusRequest>>();

        var result = (await resolvedUseCase.ExecuteAsync(new GetSubmissionStatusRequest(session.SubmissionId, ActionName: "CheckSubmission")))
            .ShouldSucceed();

        result.Status.Should().Be("Draft");
        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be("CheckSubmission");
        recorded.Outcome.Status.Should().Be("DraftAwaitingPayload");
        recorded.ProcessesConsumed.Select(p => p.ProcessName)
            .Should().Contain(["BlobStorage:GetSession", "BlobStorage:GetDraftItems", "BlobStorage:GetDraftPayload"]);
    }

    [Fact]
    public async Task NonAudited_UseCase_Should_Pass_Through_Decorator_Without_Writing_Submission_Audit()
    {
        _registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([]));

        var inner = new GetSchemasUseCase(_registry);
        var wrapped = Audited(inner);

        var result = (await wrapped.ExecuteAsync(new GetSchemasRequest())).ShouldSucceed();

        result.Schemas.Should().BeEmpty();
        await _auditStore.DidNotReceive().AppendAsync(Arg.Any<SubmissionAuditEntry>(), Arg.Any<CancellationToken>());
    }
}
