using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using AwesomeAssertions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Domain;

public class SchemaMetadataTests
{
    [Fact]
    public void When_Creating_A_Valid_Draft_Schema_It_Should_Calculate_Sha256_Hash_And_Activate()
    {
        // Act
        var meta = new SchemaMetadataBuilder().Build();

        // Assert
        meta.Sha256Hash.Should().NotBeNullOrWhiteSpace();
        meta.CanAcceptTraffic.Should().BeTrue();
        meta.State.Should().Be(SchemaLifecycleState.Active);
    }

    [Fact]
    public void When_Effective_Date_Is_In_The_Future_It_Should_Not_Be_Active_Today()
    {
        // Arrange
        var meta = new SchemaMetadataBuilder().ScheduledInFuture(5).Build();

        // Act & Assert
        meta.State.Should().Be(SchemaLifecycleState.Scheduled);
        meta.IsActiveAt(DateTimeOffset.UtcNow).Should().BeFalse();
        meta.IsActiveAt(DateTimeOffset.UtcNow.AddDays(6)).Should().BeTrue();
    }

    [Fact]
    public void When_A_Schema_Is_Deprecated_It_Should_Still_Accept_Traffic_During_Grace_Period()
    {
        // Arrange
        var meta = new SchemaMetadataBuilder().Build();

        // Act
        meta.Deprecate();

        // Assert
        meta.State.Should().Be(SchemaLifecycleState.Deprecated);
        meta.CanAcceptTraffic.Should().BeTrue();
        meta.CanBeDefault.Should().BeFalse();
    }
}
