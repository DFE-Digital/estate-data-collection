using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Contracts;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Tests.Common.Fixtures;
using AwesomeAssertions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class VerifySchemaUseCaseTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly VerifySchemaUseCase _sut;

    public VerifySchemaUseCaseTests()
    {
        _sut = new VerifySchemaUseCase(_registry);
    }

    [Fact]
    public async Task When_Schema_Syntax_And_Test_Fixtures_Pass_It_Should_Return_Successful_Verification()
    {
        // Arrange
        _registry.VerifyCanditateSchemaAsync(
            JsonSchemaFixtures.ValidV1Schema,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [])));

        var request = new VerifySchemaRequest(
            JsonSchemaFixtures.ValidV1Schema,
            [SurveyPayloadFixtures.ValidPayloadJson],
            [SurveyPayloadFixtures.MissingUrnJson]
        );

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task When_Schema_Contains_Syntax_Or_Fixture_Violations_It_Should_Return_Validation_Failed()
    {
        // Arrange
        _registry.VerifyCanditateSchemaAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult(
                ["Meta-schema violation: type must be string"],
                ["SampleValidFixtures[0] rejected by schema"]
            )));

        var request = new VerifySchemaRequest(
            "{}",
            [],
            []
        );

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.VerificationFailed");
        result.Error.ValidationErrors.Should().HaveCount(2);
    }
}
