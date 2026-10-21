using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Contracts;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Fixtures;
using AwesomeAssertions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class SubmitSchemaUseCaseTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly SubmitSchemaUseCase _sut;

    public SubmitSchemaUseCaseTests()
    {
        _sut = new SubmitSchemaUseCase(_registry);
    }

    [Fact]
    public async Task When_Schema_Version_Already_Exists_It_Should_Return_Conflict_To_Protect_Immutability()
    {
        // Arrange
        _registry.ExistsAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var request = new SubmitSchemaRequest(
            "edc_payload",
            1,
            JsonSchemaFixtures.ValidV1Schema,
            DateTimeOffset.UtcNow
        );

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.AlreadyExists");

        await _registry.DidNotReceive().SaveSchemaAsync(
            Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Pre_Flight_Verification_Fails_It_Should_Abort_Without_Saving_To_Blob_Storage()
    {
        // Arrange
        _registry.ExistsAsync("edc_payload", 2, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));

        _registry.VerifyCanditateSchemaAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult(
                ["Syntax error in schema"], [])));

        var request = new SubmitSchemaRequest(
            "edc_payload",
            2,
            "broken_schema",
            DateTimeOffset.UtcNow
        );

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.VerificationFailed");

        await _registry.DidNotReceive().SaveSchemaAsync(
            Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Effective_Date_Is_In_The_Future_It_Should_Save_With_Scheduled_State()
    {
        // Arrange
        _registry.ExistsAsync("edc_payload", 3, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));

        _registry.VerifyCanditateSchemaAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [])));

        _registry.SaveSchemaAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var futureDate = DateTimeOffset.UtcNow.AddDays(7);
        var request = new SubmitSchemaRequest(
            "edc_payload",
            3,
            JsonSchemaFixtures.ValidV2SchemaWithoutCapacity,
            futureDate
        );

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.State.Name.Should().Be("Scheduled");
        result.Value.EffectiveFromUtc.Should().Be(futureDate);

        await _registry.Received(1).SaveSchemaAsync(
            Arg.Is<SchemaMetadata>(m => m.State == SchemaLifecycleState.Scheduled && m.Version == 3),
            Arg.Any<CancellationToken>());
    }
}
