using AwesomeAssertions;
using EstateDataCollection.Domain.Submission;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Domain;

public class UrnTests
{
    [Theory]
    [InlineData(100001)]
    [InlineData(145000)]
    [InlineData(999999)]
    public void When_Creating_With_Positive_Integer_It_Should_Succeed(int value)
    {
        var result = Urn.Create(value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(value);
        ((int)result.Value).Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100234)]
    public void When_Creating_With_Zero_Or_Negative_It_Should_Fail_Validation(int value)
    {
        var result = Urn.Create(value);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payload.InvalidUrn");
    }

    [Theory]
    [InlineData("100234", 100234)]
    [InlineData("  145000  ", 145000)]
    public void When_Parsing_Valid_String_Or_Object_It_Should_Succeed(string raw, int expected)
    {
        var result = Urn.TryParse(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("100.5")]
    [InlineData("-123")]
    public void When_Parsing_Invalid_String_Or_Null_It_Should_Fail_Validation(string? raw)
    {
        var result = Urn.TryParse(raw);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payload.InvalidUrn");
    }

    [Fact]
    public void When_Extracting_From_Payload_With_Numeric_Urn_It_Should_Succeed()
    {
        var json = """{"urn": 100234, "schoolName": "St. Peters"}""";

        var result = Urn.ExtractFromPayload(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(100234);
    }

    [Fact]
    public void When_Extracting_From_Payload_With_Stringified_Urn_It_Should_Succeed()
    {
        var json = """{"urn": "100234", "schoolName": "St. Peters"}""";

        var result = Urn.ExtractFromPayload(json);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(100234);
    }

    [Fact]
    public void When_Extracting_From_Payload_Missing_Urn_It_Should_Fail_With_InvalidUrn()
    {
        var json = """{"schoolName": "St. Peters"}""";

        var result = Urn.ExtractFromPayload(json);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payload.InvalidUrn");
    }

    [Fact]
    public void When_Extracting_From_Empty_Or_Whitespace_Payload_It_Should_Fail_With_PayloadEmpty()
    {
        var result = Urn.ExtractFromPayload("   ");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Payload.Empty");
    }

    [Fact]
    public void When_Extracting_From_Malformed_Json_It_Should_Fail_With_JsonMalformed()
    {
        var result = Urn.ExtractFromPayload("{ not valid json }");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Json.Malformed");
    }
}