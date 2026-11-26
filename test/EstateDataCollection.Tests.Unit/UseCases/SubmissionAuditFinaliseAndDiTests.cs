using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class SubmissionAuditFinaliseAndDiTests
{
    private readonly ISubmissionInfrastructure _infrastructure = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _validator = Substitute.For<ISchemaValidator>();
    private readonly ISubmissionAuditStore _auditStore = Substitute.For<ISubmissionAuditStore>();

    public SubmissionAuditFinaliseAndDiTests()
    {
        _auditStore.AppendAsync(Arg.Any<SubmissionAuditEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("sub/audit/entry.json"));
    }

    private IUseCase<TResult, TRequest> Audited<TResult, TRequest>(IUseCase<TResult, TRequest> inner)
        => new SubmissionUseCaseAuditDecorator<TResult, TRequest>(inner, _auditStore);

    [Fact]
    public async Task FinaliseSubmission_When_Already_Finalised_Should_Audit_Rejected_403()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().WithIdentifier("URN", "104521").Finalised().Build();
        _infrastructure.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));

        SubmissionAuditEntry? recorded = null;
        await _auditStore.AppendAsync(Arg.Do<SubmissionAuditEntry>(e => recorded = e), Arg.Any<CancellationToken>());

        var validationService = new PayloadValidationService(_validator, new QualityRuleCoordinator());
        var evaluationService = new SubmissionEvaluationService(_infrastructure, _registry, validationService);
        var sut = Audited(new FinaliseSubmissionUseCase(_infrastructure, evaluationService));
        var request = new FinaliseSubmissionRequest(session.SubmissionId);

        // Act
        var result = await sut.ExecuteAsync(request);

        // Assert
        result.ShouldFailWith("Submission.AlreadyFinalised");
        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be("FinaliseSubmission");
        recorded.Outcome.Status.Should().Be("Rejected");
        recorded.Outcome.HttpStatus.Should().Be(403);
        recorded.Outcome.ErrorCode.Should().Be("Submission.AlreadyFinalised");
        recorded.Identifier?.Type.Should().Be("URN");
        recorded.Identifier?.Value.Should().Be("104521");
    }

    [Fact]
    public async Task FinaliseSubmission_Should_Record_Per_Process_Errors_On_ProcessesConsumed()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().WithIdentifier("URN", "104521").Build();
        var rule = new BusinessRule
        {
            Id = "RULE-CAP",
            Path = "school_capacity",
            Description = "Capacity must be positive",
            Severity = RuleSeverity.Error,
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

        var validationService = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());
        var evaluationService = new SubmissionEvaluationService(_infrastructure, _registry, validationService);
        var sut = Audited(new FinaliseSubmissionUseCase(_infrastructure, evaluationService));
        var request = new FinaliseSubmissionRequest(session.SubmissionId);

        // Act
        var result = await sut.ExecuteAsync(request);

        // Assert
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
    public async Task GetSubmissionStatus_When_Resolved_Via_Scrutor_DI_Should_Decorate_And_Audit_CheckSubmission()
    {
        // Arrange
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
        var request = new GetSubmissionStatusRequest(session.SubmissionId, ActionName: "CheckSubmission");

        // Act
        var result = (await resolvedUseCase.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        resolvedUseCase.Should().BeOfType<SubmissionUseCaseAuditDecorator<GetSubmissionStatusResult, GetSubmissionStatusRequest>>();
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
        // Arrange
        _registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([]));

        var sut = Audited(new GetSchemasUseCase(_registry));
        var request = new GetSchemasRequest();

        // Act
        var result = (await sut.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        result.Schemas.Should().BeEmpty();
        await _auditStore.DidNotReceive().AppendAsync(Arg.Any<SubmissionAuditEntry>(), Arg.Any<CancellationToken>());
    }
}
