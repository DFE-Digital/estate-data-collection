using AwesomeAssertions;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Domain;

public sealed class SubmissionIdentifierTests
{
    [Fact]
    public void InspectPayload_Should_Extract_Integer_Urn_String_Urn_And_Custom_String_Identifier()
    {
        var intUrn = SubmissionIdentifier.InspectPayload("""{"school":{"urn":123456}}""", ["/school/urn"], "URN").ShouldSucceed();
        intUrn.Status.Should().Be(IdentifierExtractionStatus.Found);
        intUrn.Identifier!.Value.Value.Should().Be("123456");
        intUrn.Identifier.Value.AsPositiveInt.Should().Be(123456);

        var strLaestab = SubmissionIdentifier.InspectPayload("""{"establishment":{"laestab":"201-3614"}}""", ["/establishment/laestab"], "LAEstab").ShouldSucceed();
        strLaestab.Status.Should().Be(IdentifierExtractionStatus.Found);
        strLaestab.Identifier!.Value.Type.Should().Be("LAEstab");
        strLaestab.Identifier.Value.Value.Should().Be("201-3614");
        strLaestab.Identifier.Value.AsPositiveInt.Should().BeNull();
    }

    [Fact]
    public void InspectPayload_Should_Distinguish_NotPresent_From_Invalid()
    {
        var missing = SubmissionIdentifier.InspectPayload("""{"other":"x"}""", ["/establishment/laestab"], "LAEstab").ShouldSucceed();
        missing.Status.Should().Be(IdentifierExtractionStatus.NotPresent);

        var explicitNull = SubmissionIdentifier.InspectPayload("""{"establishment":{"laestab":null}}""", ["/establishment/laestab"], "LAEstab").ShouldSucceed();
        explicitNull.Status.Should().Be(IdentifierExtractionStatus.NotPresent);

        var emptyStr = SubmissionIdentifier.InspectPayload("""{"establishment":{"laestab":"  "}}""", ["/establishment/laestab"], "LAEstab").ShouldSucceed();
        emptyStr.Status.Should().Be(IdentifierExtractionStatus.Invalid);

        var zeroUrn = SubmissionIdentifier.InspectPayload("""{"urn":0}""", ["/urn"], "URN").ShouldSucceed();
        zeroUrn.Status.Should().Be(IdentifierExtractionStatus.Invalid);

        var unsafeSlash = SubmissionIdentifier.InspectPayload("""{"establishment":{"laestab":"201/3614"}}""", ["/establishment/laestab"], "LAEstab").ShouldSucceed();
        unsafeSlash.Status.Should().Be(IdentifierExtractionStatus.Invalid);

        var boolWrongType = SubmissionIdentifier.InspectPayload("""{"establishment":{"laestab":true}}""", ["/establishment/laestab"], "LAEstab").ShouldSucceed();
        boolWrongType.Status.Should().Be(IdentifierExtractionStatus.Invalid);
    }
}
