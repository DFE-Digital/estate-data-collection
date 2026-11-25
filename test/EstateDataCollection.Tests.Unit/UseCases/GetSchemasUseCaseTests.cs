using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class GetSchemasUseCaseTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly GetSchemasUseCase _sut;

    public GetSchemasUseCaseTests()
    {
        _sut = new GetSchemasUseCase(_registry);
        SetupSampleVersions();
    }

    private void SetupSampleVersions()
    {
        var unscheduledMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(1).Unscheduled().Build();
        var activeMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(2).Build();
        var deprecatedMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(3).Build();
        deprecatedMeta.Deprecate();
        var upcomingMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(4).ScheduledInFuture(5).Build();
        var retiredMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(5).Retired().Build();

        _registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([unscheduledMeta, activeMeta, deprecatedMeta, upcomingMeta, retiredMeta]));
    }

    [Theory]
    [InlineData(false, false, false, new[] { 2, 3 })]
    [InlineData(false, true, false, new[] { 2, 3, 4 })]
    [InlineData(false, false, true, new[] { 2, 3, 5 })]
    [InlineData(false, true, true, new[] { 2, 3, 4, 5 })]
    [InlineData(true, true, true, new[] { 1, 2, 3, 4, 5 })]
    public async Task ExecuteAsync_Should_Filter_Versions_By_Visibility_Flags(
        bool includeUnscheduled,
        bool includeUpcoming,
        bool includeRetired,
        int[] expectedVersions)
    {
        // Arrange
        var request = new GetSchemasRequest(
            IncludeUnscheduled: includeUnscheduled,
            IncludeUpcoming: includeUpcoming,
            IncludeRetired: includeRetired);

        // Act
        var result = (await _sut.ExecuteAsync(request, CancellationToken.None)).ShouldSucceed();

        // Assert
        result.Schemas.Should().ContainSingle();
        result.Schemas[0].Versions.Select(v => v.Version).Should().BeEquivalentTo(expectedVersions);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Map_LastTestRun_Telemetry_Including_Warnings()
    {
        // Arrange
        var meta = new SchemaMetadataBuilder().WithName("cdc_survey").WithVersion(1).Build();
        meta.RecordTestRun(
            passed: true,
            appVersion: "2026.11.1+sha",
            errors: [],
            warnings: ["SampleInvalidFixtures:0 failed validation, but unknown if expected."]);

        _registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([meta]));

        var request = new GetSchemasRequest(IncludeUnscheduled: true);

        // Act
        var result = (await _sut.ExecuteAsync(request, CancellationToken.None)).ShouldSucceed();

        // Assert
        var summary = result.Schemas.Should().ContainSingle().Subject.Versions.Should().ContainSingle().Subject;
        summary.LastTestRunAtUtc.Should().NotBeNull();
        summary.LastTestRunAppVersion.Should().Be("2026.11.1+sha");
        summary.LastTestRunPassed.Should().BeTrue();
        summary.LastTestRunWarnings.Should().ContainSingle()
            .Which.Should().Contain("SampleInvalidFixtures:0");
    }
}
