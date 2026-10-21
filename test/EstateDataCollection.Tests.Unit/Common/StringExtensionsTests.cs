using EstateDataCollection.Common.Extensions;
using AwesomeAssertions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Common;

public class StringExtensionsTests
{
    [Fact]
    public void When_Formatting_With_Anonymous_Object_It_Should_Substitute_Tokens_Case_Insensitively()
    {
        // Arrange
        const string template = "{schemaName}/v{version}.schema.json";
        var values = new { SchemaName = "edc_payload", Version = 2 };

        // Act
        var result = template.FormatWith(values);

        // Assert
        result.Should().Be("edc_payload/v2.schema.json");
    }

    [Fact]
    public void When_Formatting_With_Dictionary_It_Should_Substitute_Tokens_Case_Insensitively()
    {
        // Arrange
        const string template = "urn={urn}/schema={schemaName}/v={schemaVersion}/{submissionId}.json";
        var dict = new Dictionary<string, object?>
        {
            ["URN"] = 104521,
            ["schemaname"] = "edc_payload",
            ["SCHEMAVERSION"] = 1,
            ["SubmissionId"] = "guid-1234"
        };

        // Act
        var result = template.FormatWith(dict);

        // Assert
        result.Should().Be("urn=104521/schema=edc_payload/v=1/guid-1234.json");
    }

    [Fact]
    public void When_A_Placeholder_Token_Has_No_Matching_Property_It_Should_Leave_Token_Unchanged()
    {
        // Arrange
        const string template = "urn={urn}/missing={notfound}";
        var values = new { Urn = 104521 };

        // Act
        var result = template.FormatWith(values);

        // Assert
        result.Should().Be("urn=104521/missing={notfound}");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void When_Template_Is_Null_Or_Empty_It_Should_Return_Empty_String(string? emptyTemplate)
    {
        // Act
        var result = emptyTemplate!.FormatWith(new { Foo = "bar" });

        // Assert
        result.Should().BeEmpty();
    }
}
