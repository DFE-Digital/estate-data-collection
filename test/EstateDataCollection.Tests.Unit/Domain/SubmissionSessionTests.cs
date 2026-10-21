using EstateDataCollection.Domain.Submission;
using AwesomeAssertions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Domain;

public class SubmissionSessionTests
{
    [Fact]
    public void When_Creating_A_Submission_Session_It_Should_Generate_A_UuidV7_And_Set_Draft_State()
    {
        // Act
        var session = SubmissionSession.Create("edc_payload", 2);

        // Assert
        session.SubmissionId.Should().NotBeEmpty();
        session.SchemaName.Should().Be("edc_payload");
        session.SchemaVersion.Should().Be(2);
        session.IsFinalised.Should().BeFalse();
        session.Urn.Should().BeNull();
        session.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void When_Schema_Name_Is_Null_Or_Whitespace_It_Should_Throw_Argument_Exception(string? invalidName)
    {
        // Act
        Action act = () => SubmissionSession.Create(invalidName!, 1);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-99)]
    public void When_Schema_Version_Is_Zero_Or_Negative_It_Should_Throw_Out_Of_Range_Exception(int invalidVersion)
    {
        // Act
        Action act = () => SubmissionSession.Create("edc_payload", invalidVersion);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void When_Finalising_A_Session_It_Should_Record_Timestamp_And_Mark_Finalised()
    {
        // Arrange
        var session = SubmissionSession.Create("edc_payload", 1);

        // Act
        session.Finalise();

        // Assert
        session.IsFinalised.Should().BeTrue();
        session.FinalisedAtUtc.Should().NotBeNull();
        session.FinalisedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void When_Assigning_A_Urn_It_Should_Update_The_Session_Urn()
    {
        // Arrange
        var session = SubmissionSession.Create("edc_payload", 1);

        // Act
        session.AssignUrn(104521);

        // Assert
        session.Urn.Should().Be(104521);
    }
}
