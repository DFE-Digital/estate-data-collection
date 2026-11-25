using AwesomeAssertions;
using EstateDataCollection.Domain.Schema;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Domain;

public sealed class InvalidFixtureParserTests
{
    [Fact]
    public void Parse_Raw_Payload_Should_Return_Plain_Spec_Without_ExpectedErrors()
    {
        // Arrange
        var raw = """{"urn":123456,"capacity":-1}""";

        // Act
        var spec = InvalidFixtureParser.Parse(raw);

        // Assert
        spec.HasWrapper.Should().BeFalse();
        spec.PayloadJson.Should().Be(raw);
        spec.ExpectedErrors.Should().BeEmpty();
        spec.Description.Should().BeNull();
    }

    [Fact]
    public void Parse_Wrapper_Object_Should_Extract_Payload_ExpectedErrors_And_Description()
    {
        // Arrange
        var raw = """
        {
          "description": "Grade D without repairs",
          "payload": { "urn": 123456, "condition_grade": "D", "repairs": [] },
          "expectedErrors": ["RULE-CONDITION-GRADE-D", "/elements/0"]
        }
        """;

        // Act
        var spec = InvalidFixtureParser.Parse(raw);

        // Assert
        spec.HasWrapper.Should().BeTrue();
        spec.Description.Should().Be("Grade D without repairs");
        spec.PayloadJson.Should().Contain("\"condition_grade\":\"D\"");
        spec.ExpectedErrors.Should().BeEquivalentTo(["RULE-CONDITION-GRADE-D", "/elements/0"]);
    }

    [Fact]
    public void Parse_Domain_Object_That_Contains_Payload_With_Other_Domain_Keys_Should_Not_Be_Treated_As_Wrapper()
    {
        // Arrange
        var raw = """{"payload":{"value":1},"school":{"urn":"123456"}}""";

        // Act
        var spec = InvalidFixtureParser.Parse(raw);

        // Assert
        spec.HasWrapper.Should().BeFalse();
        spec.PayloadJson.Should().Be(raw);
        spec.ExpectedErrors.Should().BeEmpty();
    }
}
