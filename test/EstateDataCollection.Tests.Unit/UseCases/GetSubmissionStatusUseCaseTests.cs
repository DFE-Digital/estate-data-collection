using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
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

public class GetSubmissionStatusUseCaseTests
{
    private readonly ISubmissionInfrastructure _repository = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _validator = Substitute.For<ISchemaValidator>();
    private readonly GetSubmissionStatusUseCase _sut;

    public GetSubmissionStatusUseCaseTests()
    {
        var validationService = new PayloadValidationService(_validator, new JsonLogicValidator());
        var evaluationService = new SubmissionEvaluationService(_repository, _registry, validationService);
        _sut = new GetSubmissionStatusUseCase(_repository, evaluationService);
    }

    [Fact]
    public async Task When_Session_Does_Not_Exist_It_Should_Return_Submission_Not_Found()
    {
        var submissionId = Guid.NewGuid();
        _repository.GetSessionAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Failure(Error.NotFound(ErrorCodes.SubmissionNotFound, new { submissionId })));

        var result = await _sut.ExecuteAsync(new GetSubmissionStatusRequest(submissionId));

        result.ShouldFailWith("Submission.NotFound");
    }

    [Fact]
    public async Task When_Submission_Is_Already_Finalised_It_Should_Return_Finalised_Status_Without_Reading_Draft()
    {
        var session = new SubmissionSessionBuilder()
            .WithUrn(104521)
            .Finalised()
            .Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));

        var result = (await _sut.ExecuteAsync(new GetSubmissionStatusRequest(session.SubmissionId))).ShouldSucceed();

        result.Status.Should().Be("Finalised");
        result.FinalisedAtUtc.Should().NotBeNull();
        result.HasDraftPayload.Should().BeFalse();
        result.IsReadyToFinalise.Should().BeFalse();
        result.Urn.Should().Be(104521);
        result.Identifier.Should().Be("104521");
        result.IdentifierType.Should().Be("URN");
        result.PendingErrors.Should().BeEmpty();
        await _repository.DidNotReceive().GetDraftPayloadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Draft_Session_Has_No_Payload_Yet_It_Should_Return_Draft_Not_Ready_With_Pending_Error()
    {
        var session = new SubmissionSessionBuilder().Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure(Error.Failure(ErrorCodes.SubmissionNotFound, new { session.SubmissionId })));

        var result = (await _sut.ExecuteAsync(new GetSubmissionStatusRequest(session.SubmissionId))).ShouldSucceed();

        result.Status.Should().Be("Draft");
        result.HasDraftPayload.Should().BeFalse();
        result.IsReadyToFinalise.Should().BeFalse();
        result.PendingErrors.Should().ContainSingle(e => e.PropertyPath == "$" && e.Message.Contains("No draft payload"));
    }

    [Fact]
    public async Task When_Draft_Payload_Has_Validation_Or_Identifier_Errors_It_Should_Return_Not_Ready_With_Pending_Errors()
    {
        var session = new SubmissionSessionBuilder().Build();
        var resolved = new ResolvedSchemaBuilder().Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(SurveyPayloadFixtures.MissingUrnJson));
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _validator.ValidateAsync(SurveyPayloadFixtures.MissingUrnJson, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult(
                [new ValidationError("#/urn", "Required property 'urn' not found")])));

        var result = (await _sut.ExecuteAsync(new GetSubmissionStatusRequest(session.SubmissionId))).ShouldSucceed();

        result.Status.Should().Be("Draft");
        result.HasDraftPayload.Should().BeTrue();
        result.IsReadyToFinalise.Should().BeFalse();
        result.PendingErrors.Should().NotBeEmpty();
        result.PendingErrors.Should().Contain(e => e.PropertyPath == "#/urn");
    }

    [Fact]
    public async Task When_Draft_Payload_Is_Valid_It_Should_Return_Ready_To_Finalise_With_Extracted_Identifier()
    {
        var session = new SubmissionSessionBuilder().Build();
        var resolved = new ResolvedSchemaBuilder().Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(SurveyPayloadFixtures.ValidPayloadJson));
        _registry.GetExplicitSchemaAsync(session.SchemaName, session.SchemaVersion, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _validator.ValidateAsync(SurveyPayloadFixtures.ValidPayloadJson, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));

        var result = (await _sut.ExecuteAsync(new GetSubmissionStatusRequest(session.SubmissionId))).ShouldSucceed();

        result.Status.Should().Be("Draft");
        result.HasDraftPayload.Should().BeTrue();
        result.IsReadyToFinalise.Should().BeTrue();
        result.PendingErrors.Should().BeEmpty();
        result.Urn.Should().Be(104521);
        result.Identifier.Should().Be("104521");
        result.IdentifierType.Should().Be("URN");
    }
}
