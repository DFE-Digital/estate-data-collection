using AwesomeAssertions;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Domain;

public sealed class SubmissionIdentifierTests
{
    [Fact]
    public void InspectPayload_Should_Extract_Integer_Identifier()
    {
        // Arrange
        const string payload = """{"school":{"urn":123456}}""";

        // Act
        var extraction = SubmissionIdentifier.InspectPayload(payload, ["/school/urn"], "URN").ShouldSucceed();

        // Assert
        extraction.Status.Should().Be(IdentifierExtractionStatus.Found);
        extraction.Identifier!.Value.Value.Should().Be("123456");
        extraction.Identifier.Value.AsPositiveInt.Should().Be(123456);
    }

    [Fact]
    public void InspectPayload_Should_Extract_Zero_Scalar_Identifier()
    {
        // Arrange
        const string payload = """{"urn":0}""";

        // Act
        var extraction = SubmissionIdentifier.InspectPayload(payload, ["/urn"], "URN").ShouldSucceed();

        // Assert
        extraction.Status.Should().Be(IdentifierExtractionStatus.Found);
        extraction.Identifier!.Value.Value.Should().Be("0");
    }

    [Fact]
    public void InspectPayload_Should_Extract_Custom_String_Identifier()
    {
        // Arrange
        const string payload = """{"establishment":{"laestab":"201-3614"}}""";

        // Act
        var extraction = SubmissionIdentifier.InspectPayload(payload, ["/establishment/laestab"], "LAEstab").ShouldSucceed();

        // Assert
        extraction.Status.Should().Be(IdentifierExtractionStatus.Found);
        extraction.Identifier!.Value.Type.Should().Be("LAEstab");
        extraction.Identifier.Value.Value.Should().Be("201-3614");
        extraction.Identifier.Value.AsPositiveInt.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"other":"x"}""", IdentifierExtractionStatus.NotPresent)]
    [InlineData("""{"establishment":{"laestab":null}}""", IdentifierExtractionStatus.NotPresent)]
    [InlineData("""{"establishment":{"laestab":"  "}}""", IdentifierExtractionStatus.Invalid)]
    [InlineData("""{"establishment":{"laestab":"201/3614"}}""", IdentifierExtractionStatus.Invalid)]
    [InlineData("""{"establishment":{"laestab":true}}""", IdentifierExtractionStatus.Invalid)]
    public void InspectPayload_Should_Distinguish_NotPresent_From_Invalid(
        string payloadJson,
        IdentifierExtractionStatus expectedStatus)
    {
        // Arrange — payloadJson supplied by theory data

        // Act
        var result = SubmissionIdentifier.InspectPayload(payloadJson, ["/establishment/laestab"], "LAEstab").ShouldSucceed();

        // Assert
        result.Status.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData("URN", "123/456")]
    [InlineData("CustomId", "  ")]
    public void Create_Should_Emit_Generic_PayloadInvalidIdentifier_For_Any_Type(string type, string value)
    {
        // Arrange — type and value supplied by theory data

        // Act
        var result = SubmissionIdentifier.Create(type, value);

        // Assert
        result.ShouldFailWith("Payload.InvalidIdentifier");
    }
}
