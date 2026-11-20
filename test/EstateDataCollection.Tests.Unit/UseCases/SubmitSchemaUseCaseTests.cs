using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
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
            JsonSchemaFixtures.ValidV1Schema
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
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult(
                ["Syntax error in schema"], [])));

        var request = new SubmitSchemaRequest(
            "edc_payload",
            2,
            "broken_schema"
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
    public async Task When_Schema_Is_Submitted_It_Should_Save_In_Validated_Unscheduled_State()
    {
        // Arrange
        _registry.ExistsAsync("edc_payload", 3, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));

        _registry.VerifyCanditateSchemaAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [])));

        _registry.SaveSchemaAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new SubmitSchemaRequest(
            "edc_payload",
            3,
            JsonSchemaFixtures.ValidV2SchemaWithoutCapacity
        );

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.State.Name.Should().Be("Validated");
        result.Value.EffectiveFromUtc.Should().BeNull();
        result.Value.EffectiveToUtc.Should().BeNull();

        await _registry.Received(1).SaveSchemaAsync(
            Arg.Is<SchemaMetadata>(m => m.State == SchemaLifecycleState.Validated && m.Version == 3 && m.EffectiveFromUtc == null),
            Arg.Any<CancellationToken>());
    }
}
