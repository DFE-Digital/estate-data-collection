using System.Text;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.AzureFunctions.Models.Results;
using EstateDataCollection.AzureFunctions.Routes;
using EstateDataCollection.Common.Results;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
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

    private readonly SubmissionFunctions _sut;

    public SubmissionFunctionsTests()
    {
        _sut = new SubmissionFunctions(_createUseCase, _updateUseCase, _finaliseUseCase);
    }

    [Fact]
    public async Task When_Creating_A_Submission_Session_With_Explicit_Version_It_Should_Return_201_Created_With_Session_Details()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        var expectedResult = new CreateSubmissionResult(expectedId, "edc_payload", 2, DateTimeOffset.UtcNow);

        _createUseCase.ExecuteAsync(
            Arg.Is<CreateSubmissionRequest>(r => r.SchemaName == "edc_payload" && r.Version == 2),
            Arg.Any<CancellationToken>())
            .Returns(Result<CreateSubmissionResult>.Success(expectedResult));

        var request = CreateHttpRequest(string.Empty, new QueryCollection(new Dictionary<string, StringValues>
        {
            ["version"] = "2"
        }));

        // Act
        var actionResult = await _sut.CreateSubmission(request, "edc_payload", CancellationToken.None);

        // Assert
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status201Created);

        var response = objectResult.Value.Should().BeOfType<ApiResponse<CreateSubmissionResult>>().Subject;
        response.Successful.Should().BeTrue();
        response.Data!.SubmissionId.Should().Be(expectedId);
        response.Data.Version.Should().Be(2);
    }

    [Fact]
    public async Task When_Creating_A_Submission_Session_Fails_It_Should_Return_Problem_Details()
    {
        // Arrange
        _createUseCase.ExecuteAsync(Arg.Any<CreateSubmissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateSubmissionResult>.Failure(Error.NotFound("Schema.NotFound", "Schema not found.")));

        var request = CreateHttpRequest(string.Empty);

        // Act
        var actionResult = await _sut.CreateSubmission(request, "unknown_schema", CancellationToken.None);

        // Assert
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var response = objectResult.Value.Should().BeOfType<ApiResponse<CreateSubmissionResult>>().Subject;
        response.Successful.Should().BeFalse();
        response.Problem.Should().NotBeNull();
        response.Problem!.Title.Should().Be("Schema.NotFound");
    }

    [Fact]
    public async Task When_Updating_With_Empty_Payload_Body_It_Should_Return_403_Forbidden_Payload_Empty()
    {
        // Arrange
        var request = CreateHttpRequest("   ");

        // Act
        var actionResult = await _sut.UpdateSubmission(request, Guid.NewGuid(), CancellationToken.None);

        // Assert
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        await _updateUseCase.DidNotReceive().ExecuteAsync(
            Arg.Any<UpdateSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Updating_With_Partial_Draft_It_Should_Return_200_OK_With_Pending_Validation_Errors()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var pendingErrors = new List<ValidationError>
        {
            new("#/school_address", "Required property 'school_address' not found")
        };

        var updateResult = new UpdateSubmissionResult(
            SubmissionId: submissionId,
            IsSuccessful: false,
            PendingErrors: pendingErrors,
            SavedAtUtc: DateTimeOffset.UtcNow
        );

        _updateUseCase.ExecuteAsync(
            Arg.Is<UpdateSubmissionRequest>(r => r.SubmissionId == submissionId),
            Arg.Any<CancellationToken>())
            .Returns(Result<UpdateSubmissionResult>.Success(updateResult));

        var request = CreateHttpRequest("{\"urn\":104521}");

        // Act
        var actionResult = await _sut.UpdateSubmission(request, submissionId, CancellationToken.None);

        // Assert
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status200OK);

        var response = objectResult.Value.Should().BeOfType<ApiResponse<UpdateSubmissionResult>>().Subject;
        response.Successful.Should().BeTrue();
        response.Data!.IsSuccessful.Should().BeFalse();
        response.Data.PendingErrors.Should().HaveCount(1);
    }

    [Fact]
    public async Task When_Finalising_A_Complete_Survey_It_Should_Return_200_OK_With_Processed_Blob_Uri()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var finaliseResult = new FinaliseSubmissionResult(
            SubmissionId: submissionId,
            Urn: 104521,
            SchemaName: "edc_payload",
            SchemaVersion: 1,
            Status: "ReadyForCde",
            ProcessedBlobUri: "https://storage/processed/urn=104521/final.json",
            FinalisedAtUtc: DateTimeOffset.UtcNow
        );

        _finaliseUseCase.ExecuteAsync(
            Arg.Is<FinaliseSubmissionRequest>(r => r.SubmissionId == submissionId),
            Arg.Any<CancellationToken>())
            .Returns(Result<FinaliseSubmissionResult>.Success(finaliseResult));

        var request = CreateHttpRequest(string.Empty);

        // Act
        var actionResult = await _sut.FinaliseSubmission(request, submissionId, CancellationToken.None);

        // Assert
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status200OK);

        var response = objectResult.Value.Should().BeOfType<ApiResponse<FinaliseSubmissionResult>>().Subject;
        response.Successful.Should().BeTrue();
        response.Data!.Urn.Should().Be(104521);
        response.Data.ProcessedBlobUri.Should().Contain("urn=104521");
    }

    [Fact]
    public async Task When_Finalising_An_Incomplete_Survey_It_Should_Return_422_Unprocessable_Entity()
    {
        // Arrange
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

        var request = CreateHttpRequest(string.Empty);

        // Act
        var actionResult = await _sut.FinaliseSubmission(request, submissionId, CancellationToken.None);

        // Assert
        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);

        var response = objectResult.Value.Should().BeOfType<ApiResponse<FinaliseSubmissionResult>>().Subject;
        response.Successful.Should().BeFalse();
        response.Problem.Should().NotBeNull();
        response.Problem!.Title.Should().Be("Schema.ContractViolation");
    }

    private static HttpRequest CreateHttpRequest(string body, IQueryCollection? query = null)
    {
        var context = new DefaultHttpContext();
        if (query is not null)
        {
            context.Request.Query = query;
        }

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.Body = stream;
        context.Request.ContentLength = stream.Length;
        context.Request.ContentType = "application/json";

        return context.Request;
    }
}
