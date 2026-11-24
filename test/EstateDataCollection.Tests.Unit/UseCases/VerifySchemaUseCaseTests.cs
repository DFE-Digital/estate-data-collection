using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Fixtures;
using AwesomeAssertions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class VerifySchemaUseCaseTests
{
    private readonly ISchemaVerificationService _verifier = Substitute.For<ISchemaVerificationService>();
    private readonly VerifySchemaUseCase _sut;

    public VerifySchemaUseCaseTests()
    {
        _sut = new VerifySchemaUseCase(_verifier);
    }

    [Fact]
    public async Task When_Schema_Syntax_And_Test_Fixtures_Pass_It_Should_Return_Successful_Verification_With_EngineTraces()
    {
        // Arrange
        ValidationEngineTrace[] traces =
        [
            new(
                new ValidationEngineDescriptor("json-schema", "JsonSchema.Net Validator", ValidationEngineKinds.StructuralSchema, "Validates structure"),
                "Succeeded",
                ItemsEvaluated: 3,
                ViolationCount: 0,
                DurationMs: 2,
                Summary: "Schema valid; checked 1 valid and 1 invalid fixture(s)")
        ];

        _verifier.VerifyCandidateSchemaAsync(
            JsonSchemaFixtures.ValidV1Schema,
            null,
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<SchemaIdentifierConfig?>(),
            Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [], EngineTraces: traces)));

        var request = new VerifySchemaRequest(
            JsonSchemaFixtures.ValidV1Schema,
            [SurveyPayloadFixtures.ValidPayloadJson],
            [SurveyPayloadFixtures.MissingUrnJson]
        );

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.EngineTraces.Should().NotBeNull().And.ContainSingle(t => t.Descriptor.EngineId == "json-schema");
    }

    [Fact]
    public async Task When_Schema_Contains_Syntax_Or_Fixture_Violations_It_Should_Return_Validation_Failed()
    {
        // Arrange
        _verifier.VerifyCandidateSchemaAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<SchemaIdentifierConfig?>(),
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
