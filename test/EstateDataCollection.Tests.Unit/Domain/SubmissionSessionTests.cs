using EstateDataCollection.Domain.Submission;
using AwesomeAssertions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Domain;

public class SubmissionSessionTests
{
    [Fact]
    public void When_Creating_A_Submission_Session_It_Should_Generate_A_UuidV7_And_Set_Draft_State()
    {
        // Arrange
        const string schemaName = "edc_payload";
        const int schemaVersion = 2;

        // Act
        var session = SubmissionSession.Create(schemaName, schemaVersion);

        // Assert
        session.SubmissionId.Should().NotBeEmpty();
        session.SchemaName.Should().Be(schemaName);
        session.SchemaVersion.Should().Be(schemaVersion);
        session.IsFinalised.Should().BeFalse();
        session.IdentifierType.Should().BeNull();
        session.IdentifierValue.Should().BeNull();
        session.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void When_Schema_Name_Is_Null_Or_Whitespace_It_Should_Throw_Argument_Exception(string? invalidName)
    {
        // Arrange
        const int schemaVersion = 1;

        // Act
        Action act = () => SubmissionSession.Create(invalidName!, schemaVersion);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-99)]
    public void When_Schema_Version_Is_Zero_Or_Negative_It_Should_Throw_Out_Of_Range_Exception(int invalidVersion)
    {
        // Arrange
        const string schemaName = "edc_payload";

        // Act
        Action act = () => SubmissionSession.Create(schemaName, invalidVersion);

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
    public void When_Assigning_An_Identifier_It_Should_Trim_And_Store_Type_And_Value()
    {
        // Arrange
        var session = SubmissionSession.Create("edc_payload", 1);

        // Act
        session.AssignIdentifier(" URN ", " 104521 ");

        // Assert
        session.IdentifierType.Should().Be("URN");
        session.IdentifierValue.Should().Be("104521");
    }
}
