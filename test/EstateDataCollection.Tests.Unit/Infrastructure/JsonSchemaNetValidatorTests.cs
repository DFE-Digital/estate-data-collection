using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Fixtures;
using AwesomeAssertions;
using Json.Schema;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class JsonSchemaNetValidatorTests
{
    private readonly JsonSchemaNetValidator _sut = new();

    [Fact]
    public async Task When_Payload_Conforms_To_Schema_It_Should_Return_Valid_Result()
    {
        // Arrange
        var schema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);

        // Act
        var result = await _sut.ValidateAsync(SurveyPayloadFixtures.ValidPayloadJson, schema);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeTrue();
        result.Value.Errors.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task When_Required_Properties_Are_Missing_It_Should_Flag_Validation_Errors()
    {
        // Arrange
        var schema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);

        // Act
        var result = await _sut.ValidateAsync(SurveyPayloadFixtures.MissingUrnJson, schema);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e => e.Message.Contains("urn") || e.Message.Contains("required"));
    }

    [Fact]
    public async Task When_Undeclared_Properties_Are_Passed_It_Should_Reject_Under_Closed_Schema_Rule()
    {
        // Arrange
        var schema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);

        // Act
        var result = await _sut.ValidateAsync(SurveyPayloadFixtures.AdditionalPropertyJson, schema);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.Errors.Should().Contain(e => e.Message.Contains("unexpected_extra_field") || e.Message.Contains("additionalProperties") || e.PropertyPath.Contains("unexpected_extra_field"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task When_Payload_String_Is_Empty_Or_Whitespace_It_Should_Return_Empty_Payload_Error(string emptyPayload)
    {
        // Arrange
        var schema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);

        // Act
        var result = await _sut.ValidateAsync(emptyPayload, schema);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Json.Empty");
    }

    [Fact]
    public async Task When_Payload_Has_Malformed_Json_Syntax_It_Should_Return_Json_Malformed_Error()
    {
        // Arrange
        var schema = JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema);
        var brokenJson = "{ \"urn\": 12345, missing_quotes_and_braces: ";

        // Act
        var result = await _sut.ValidateAsync(brokenJson, schema);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Json.Malformed");
    }
}
