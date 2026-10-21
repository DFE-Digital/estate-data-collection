using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;
using AwesomeAssertions;
using Json.Schema;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class UpdateSubmissionUseCaseTests
{
    private readonly ISubmissionInfrastructure _infrastructure = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _validator = Substitute.For<ISchemaValidator>();
    private readonly UpdateSubmissionUseCase _sut;

    public UpdateSubmissionUseCaseTests()
    {
        _sut = new UpdateSubmissionUseCase(_infrastructure, _registry, _validator);
    }

    [Fact]
    public async Task When_Session_Id_Does_Not_Exist_It_Should_Return_Not_Found()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        _infrastructure.GetSessionAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Failure(Error.NotFound(ErrorCodes.SubmissionNotFound)));

        var request = new UpdateSubmissionRequest(submissionId, SurveyPayloadFixtures.ValidPayloadJson);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Submission.NotFound");
    }

    [Fact]
    public async Task When_Session_Is_Already_Finalised_It_Should_Reject_Modifications_With_Forbidden()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var session = SubmissionSession.Create("edc_payload", 1);
        session.Finalise(); // Mark as finalized

        _infrastructure.GetSessionAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));

        var request = new UpdateSubmissionRequest(submissionId, SurveyPayloadFixtures.ValidPayloadJson);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Submission.AlreadyFinalised");

        await _infrastructure.DidNotReceive().SaveDraftPayloadAsync(
            Arg.Any<Guid>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Draft_Payload_Is_Incomplete_It_Should_Still_Persist_Draft_And_Return_Pending_Errors()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var session = SubmissionSession.Create("edc_payload", 1);

        _infrastructure.GetSessionAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));

        var schemaMetadata = new SchemaMetadataBuilder().Build();
        var compiledSchema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);

        _registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(schemaMetadata, compiledSchema)));

        var pendingErrors = new List<ValidationError>
        {
            new("#/school_address", "Required property 'school_address' not found")
        };

        _validator.ValidateAsync(SurveyPayloadFixtures.MissingUrnJson, compiledSchema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult(pendingErrors)));

        _infrastructure.SaveDraftPayloadAsync(submissionId, null, SurveyPayloadFixtures.MissingUrnJson, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpdateSubmissionRequest(submissionId, SurveyPayloadFixtures.MissingUrnJson);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsSuccessful.Should().BeFalse(); // Not completely valid yet
        result.Value.PendingErrors.Should().HaveCount(1);
        result.Value.PendingErrors[0].PropertyPath.Should().Be("#/school_address");

        // Verify draft WAS saved to storage despite being incomplete
        await _infrastructure.Received(1).SaveDraftPayloadAsync(
            submissionId, null, SurveyPayloadFixtures.MissingUrnJson, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Draft_Payload_Is_Complete_And_Valid_It_Should_Persist_Draft_With_No_Pending_Errors()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var session = SubmissionSession.Create("edc_payload", 1);

        _infrastructure.GetSessionAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));

        var schemaMetadata = new SchemaMetadataBuilder().Build();
        var compiledSchema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);

        _registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(schemaMetadata, compiledSchema)));

        _validator.ValidateAsync(SurveyPayloadFixtures.ValidPayloadJson, compiledSchema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));

        _infrastructure.SaveDraftPayloadAsync(submissionId, null, SurveyPayloadFixtures.ValidPayloadJson, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new UpdateSubmissionRequest(submissionId, SurveyPayloadFixtures.ValidPayloadJson);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsSuccessful.Should().BeTrue();
        result.Value.PendingErrors.Should().BeEmpty();
    }
}
