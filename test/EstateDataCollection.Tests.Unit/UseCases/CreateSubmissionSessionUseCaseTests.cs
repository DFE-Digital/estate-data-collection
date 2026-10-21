using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;
using AwesomeAssertions;
using Json.Schema;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class CreateSubmissionSessionUseCaseTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISubmissionInfrastructure _infrastructure = Substitute.For<ISubmissionInfrastructure>();
    private readonly CreateSubmissionUseCase _sut;

    public CreateSubmissionSessionUseCaseTests()
    {
        _sut = new CreateSubmissionUseCase(_registry, _infrastructure);
    }

    [Fact]
    public async Task When_Requesting_An_Explicit_Active_Version_It_Should_Create_Session_With_Pinned_Version()
    {
        // Arrange
        var schemaMetadata = new SchemaMetadataBuilder().WithVersion(2).Build();
        var compiledSchema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);

        _registry.GetExplicitSchemaAsync("edc_payload", 2, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(schemaMetadata, compiledSchema)));

        _infrastructure.CreateSessionAsync(Arg.Any<SubmissionSession>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Result<Guid>.Success(callInfo.Arg<SubmissionSession>().SubmissionId));

        var request = new CreateSubmissionRequest("edc_payload", 2);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SchemaName.Should().Be("edc_payload");
        result.Value.Version.Should().Be(2);
        result.Value.SubmissionId.Should().NotBeEmpty();

        await _infrastructure.Received(1).CreateSessionAsync(
            Arg.Is<SubmissionSession>(s => s.SchemaName == "edc_payload" && s.SchemaVersion == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Requested_Schema_Version_Does_Not_Exist_It_Should_Return_Not_Found_And_Not_Persist()
    {
        // Arrange
        _registry.GetExplicitSchemaAsync("non_existent", 99, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Failure(Error.NotFound(ErrorCodes.SchemaNotFound)));

        var request = new CreateSubmissionRequest("non_existent", 99);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.NotFound");

        await _infrastructure.DidNotReceive().CreateSessionAsync(
            Arg.Any<SubmissionSession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Requested_Schema_Version_Is_Retired_It_Should_Reject_With_Conflict_Error()
    {
        // Arrange
        var schemaMetadata = new SchemaMetadataBuilder().WithVersion(1).Build();
        schemaMetadata.Retire();

        var compiledSchema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);
        _registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(schemaMetadata, compiledSchema)));

        var request = new CreateSubmissionRequest("edc_payload", 1);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.Retired");

        await _infrastructure.DidNotReceive().CreateSessionAsync(
            Arg.Any<SubmissionSession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_No_Version_Specified_It_Should_Resolve_The_Currently_Active_Scheduled_Version()
    {
        // Arrange
        var schemaMetadata = new SchemaMetadataBuilder().WithVersion(1).Build();
        var compiledSchema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);

        _registry.ResolveActiveSchemaAsync("edc_payload", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(schemaMetadata, compiledSchema)));

        _infrastructure.CreateSessionAsync(Arg.Any<SubmissionSession>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Result<Guid>.Success(callInfo.Arg<SubmissionSession>().SubmissionId));

        var request = new CreateSubmissionRequest("edc_payload", null);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(1);
    }
}
