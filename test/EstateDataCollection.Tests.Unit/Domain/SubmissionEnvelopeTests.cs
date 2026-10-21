using EstateDataCollection.Domain.Submission;
using AwesomeAssertions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Domain;

public class SubmissionEnvelopeTests
{
    [Fact]
    public void When_Creating_A_Submission_Envelope_It_Should_Generate_UuidV7_And_Compute_Sha256()
    {
        // Arrange
        const int urn = 104521;
        const string schemaName = "edc_payload";
        const int version = 1;
        const string json = "{\"urn\":104521}";
        var submittedAt = DateTimeOffset.UtcNow;

        // Act
        var envelope = SubmissionEnvelope.Create(urn, schemaName, version, json, submittedAt);

        // Assert
        envelope.SubmissionId.Should().NotBeEmpty();
        envelope.Urn.Should().Be(urn);
        envelope.SchemaName.Should().Be(schemaName);
        envelope.SchemaVersion.Should().Be(version);
        envelope.RawPayloadJson.Should().Be(json);
        envelope.PayloadSha256Hash.Should().NotBeNullOrWhiteSpace();
        envelope.SubmittedAtUtc.Should().Be(submittedAt);
    }

    [Fact]
    public void When_Payloads_Have_Identical_Content_They_Should_Produce_Identical_Sha256_Hashes()
    {
        // Arrange
        const string json = "{\"test\": 123}";
        var now = DateTimeOffset.UtcNow;

        // Act
        var envelope1 = SubmissionEnvelope.Create(100, "edc", 1, json, now);
        var envelope2 = SubmissionEnvelope.Create(100, "edc", 1, json, now);

        // Assert
        envelope1.PayloadSha256Hash.Should().Be(envelope2.PayloadSha256Hash);
        envelope1.SubmissionId.Should().NotBe(envelope2.SubmissionId); // Distinct UUIDv7
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void When_Urn_Is_Zero_Or_Negative_It_Should_Throw_Out_Of_Range_Exception(int invalidUrn)
    {
        // Act
        Action act = () => SubmissionEnvelope.Create(invalidUrn, "edc", 1, "{}", DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void When_Schema_Name_Is_Null_Or_Empty_It_Should_Throw_Argument_Exception(string? invalidName)
    {
        // Act
        Action act = () => SubmissionEnvelope.Create(100, invalidName!, 1, "{}", DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void When_Payload_Json_Is_Null_Or_Empty_It_Should_Throw_Argument_Exception(string? invalidPayload)
    {
        // Act
        Action act = () => SubmissionEnvelope.Create(100, "edc", 1, invalidPayload!, DateTimeOffset.UtcNow);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
