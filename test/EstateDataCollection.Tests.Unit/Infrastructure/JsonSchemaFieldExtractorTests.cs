using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class JsonSchemaFieldExtractorTests
{
    [Fact]
    public void ExtractFields_Should_Extract_Required_Constraints_And_Nested_Fields()
    {
        // Arrange
        const string schemaWithConstraintsAndNesting = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["urn", "survey"],
          "properties": {
            "urn": {
              "type": "integer",
              "minimum": 100000,
              "maximum": 999999,
              "description": "Unique Reference Number"
            },
            "survey": {
              "type": "object",
              "required": ["status"],
              "properties": {
                "status": {
                  "type": "string",
                  "enum": ["OPEN", "CLOSED"]
                }
              }
            }
          }
        }
        """;

        // Act
        var fields = JsonSchemaFieldExtractor.ExtractFields(schemaWithConstraintsAndNesting);

        // Assert
        fields.Should().HaveCount(2);
        var urnField = fields.FirstOrDefault(f => f.Name == "urn");
        urnField.Should().NotBeNull();
        urnField!.Required.Should().BeTrue();
        urnField.Type.Should().Be("integer");
        urnField.Path.Should().Be("urn");
        urnField.Description.Should().Be("Unique Reference Number");
        urnField.Constraints.Should().NotBeNull();
        urnField.Constraints!.Minimum.Should().Be(100000);
        urnField.Constraints.Maximum.Should().Be(999999);

        var surveyField = fields.FirstOrDefault(f => f.Name == "survey");
        surveyField.Should().NotBeNull();
        surveyField!.Children.Should().ContainSingle(c =>
            c.Name == "status" &&
            c.Path == "survey.status" &&
            c.Required &&
            c.Constraints != null &&
            c.Constraints.EnumValues != null &&
            c.Constraints.EnumValues.Contains("OPEN"));
    }
}
