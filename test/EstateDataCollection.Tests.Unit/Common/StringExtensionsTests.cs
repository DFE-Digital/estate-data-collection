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
    public void When_Formatting_Route_Templates_With_Constraints_Or_CatchAll_It_Should_Substitute_Tokens()
    {
        // Arrange
        var submissionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        const string template = "manage/schemas/{schemaName}/{version:int}/submissions/{submissionId:guid}";
        const string catchAllTemplate = "/assets/{*assetPath}";

        // Act
        var result = template.FormatWith(new
        {
            schemaName = "edc_payload",
            version = 3,
            submissionId
        });
        var assetResult = catchAllTemplate.FormatWith(new { assetPath = "js/workbench.js" });

        // Assert
        result.Should().Be($"manage/schemas/edc_payload/3/submissions/{submissionId}");
        assetResult.Should().Be("/assets/js/workbench.js");
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
        // Arrange
        var values = new { Foo = "bar" };

        // Act
        var result = emptyTemplate!.FormatWith(values);

        // Assert
        result.Should().BeEmpty();
    }
}
