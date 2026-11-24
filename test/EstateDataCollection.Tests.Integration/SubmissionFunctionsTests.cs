using AwesomeAssertions;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Integration;

public class SubmissionFunctionsTests
{
    private readonly IUseCase<CreateSubmissionResult, CreateSubmissionRequest> _createUseCase =
        Substitute.For<IUseCase<CreateSubmissionResult, CreateSubmissionRequest>>();
    private readonly IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest> _updateUseCase =
        Substitute.For<IUseCase<UpdateSubmissionResult, UpdateSubmissionRequest>>();
    private readonly IUseCase<FinaliseSubmissionResult, FinaliseSubmissionRequest> _finaliseUseCase =
        Substitute.For<IUseCase<FinaliseSubmissionResult, FinaliseSubmissionRequest>>();

    private readonly PostCreateSubmissionEndpoint _createEndpoint;
    private readonly PutUpdateSubmissionEndpoint _updateEndpoint;
    private readonly PostFinaliseSubmissionEndpoint _finaliseEndpoint;

    public SubmissionFunctionsTests()
    {
        _createEndpoint = new PostCreateSubmissionEndpoint(_createUseCase);
        _updateEndpoint = new PutUpdateSubmissionEndpoint(_updateUseCase);
        _finaliseEndpoint = new PostFinaliseSubmissionEndpoint(_finaliseUseCase);
    }

    [Fact]
    public async Task When_Creating_A_Submission_Session_With_Explicit_Version_It_Should_Return_201_Created_With_Session_Details()
    {
        var expectedId = Guid.NewGuid();
        var expectedResult = new CreateSubmissionResult(expectedId, "edc_payload", 2, DateTimeOffset.UtcNow);

        _createUseCase.ExecuteAsync(
            Arg.Is<CreateSubmissionRequest>(r => r.SchemaName == "edc_payload" && r.Version == 2),
            Arg.Any<CancellationToken>())
            .Returns(Result<CreateSubmissionResult>.Success(expectedResult));

        var request = new HttpRequestBuilder().WithQuery("version", "2").Build();

        var actionResult = await _createEndpoint.HandleAsync(request, "edc_payload", CancellationToken.None);

        var data = actionResult.ShouldBeApiSuccess<CreateSubmissionResult>(StatusCodes.Status201Created);
        data.SubmissionId.Should().Be(expectedId);
        data.Version.Should().Be(2);
    }

    [Fact]
    public async Task When_Creating_A_Submission_Session_Fails_It_Should_Return_Problem_Details()
    {
        _createUseCase.ExecuteAsync(Arg.Any<CreateSubmissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateSubmissionResult>.Failure(Error.NotFound("Schema.NotFound", "Schema not found.")));

        var actionResult = await _createEndpoint.HandleAsync(HttpRequestBuilder.Empty(), "unknown_schema", CancellationToken.None);

        actionResult.ShouldBeApiFailure<CreateSubmissionResult>(StatusCodes.Status404NotFound, "Schema.NotFound");
    }

    [Fact]
    public async Task When_Updating_With_Empty_Payload_Body_It_Should_Return_400_BadRequest_Payload_Empty()
    {
        var actionResult = await _updateEndpoint.HandleAsync(
            HttpRequestBuilder.FromRaw("   "),
            Guid.NewGuid(),
            CancellationToken.None);

        actionResult.ShouldBeApiFailure<UpdateSubmissionResult>(StatusCodes.Status400BadRequest, "Payload.Empty");
        await _updateUseCase.DidNotReceive().ExecuteAsync(
            Arg.Any<UpdateSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Updating_With_Partial_Draft_It_Should_Return_200_OK_With_Pending_Validation_Errors()
    {
        var submissionId = Guid.NewGuid();
        var updateResult = new UpdateSubmissionResult(
            SubmissionId: submissionId,
            IsSuccessful: false,
            PendingErrors: [new("#/school_address", "Required property 'school_address' not found")],
            SavedAtUtc: DateTimeOffset.UtcNow);

        _updateUseCase.ExecuteAsync(
            Arg.Is<UpdateSubmissionRequest>(r => r.SubmissionId == submissionId),
            Arg.Any<CancellationToken>())
            .Returns(Result<UpdateSubmissionResult>.Success(updateResult));

        var actionResult = await _updateEndpoint.HandleAsync(
            HttpRequestBuilder.FromRaw("{\"urn\":104521}"),
            submissionId,
            CancellationToken.None);

        var data = actionResult.ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        data.IsSuccessful.Should().BeFalse();
        data.PendingErrors.Should().HaveCount(1);
    }

    [Fact]
    public async Task When_Finalising_A_Complete_Survey_It_Should_Return_200_OK_With_Processed_Blob_Uri()
    {
        var submissionId = Guid.NewGuid();
        var finaliseResult = new FinaliseSubmissionResult(
            SubmissionId: submissionId,
            SchemaName: "edc_payload",
            SchemaVersion: 1,
            Status: "Finalised",
            ProcessedBlobUri: "https://storage/processed/urn=104521/final.json",
            FinalisedAtUtc: DateTimeOffset.UtcNow,
            Identifier: "104521",
            IdentifierType: "URN");

        _finaliseUseCase.ExecuteAsync(
            Arg.Is<FinaliseSubmissionRequest>(r => r.SubmissionId == submissionId),
            Arg.Any<CancellationToken>())
            .Returns(Result<FinaliseSubmissionResult>.Success(finaliseResult));

        var actionResult = await _finaliseEndpoint.HandleAsync(HttpRequestBuilder.Empty(), submissionId, CancellationToken.None);

        var data = actionResult.ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);
        data.Identifier.Should().Be("104521");
        data.IdentifierType.Should().Be("URN");
        data.ProcessedBlobUri.Should().Contain("urn=104521");
    }

    [Fact]
    public async Task When_Finalising_An_Incomplete_Survey_It_Should_Return_422_Unprocessable_Entity()
    {
        var submissionId = Guid.NewGuid();
        var validationErrors = new List<ValidationError>
        {
            new("#/school_address", "Required property 'school_address' not found")
        };

        _finaliseUseCase.ExecuteAsync(
            Arg.Is<FinaliseSubmissionRequest>(r => r.SubmissionId == submissionId),
            Arg.Any<CancellationToken>())
            .Returns(Result<FinaliseSubmissionResult>.Failure(
                Error.Validation("Schema.ContractViolation", "Contract violated", validationErrors)));

        var actionResult = await _finaliseEndpoint.HandleAsync(HttpRequestBuilder.Empty(), submissionId, CancellationToken.None);

        actionResult.ShouldBeApiFailure<FinaliseSubmissionResult>(StatusCodes.Status422UnprocessableEntity, "Schema.ContractViolation");
    }
}
