using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.GetValidationEngines;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class GetSchemaDetailAndEnginesUseCaseTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();

    public GetSchemaDetailAndEnginesUseCaseTests()
    {
        SetupSchemaVersions();
    }

    private void SetupSchemaVersions()
    {
        var unscheduledResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").WithVersion(1).Unscheduled())
            .Build();
        var upcomingResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").WithVersion(2).ScheduledInFuture(3))
            .Build();
        var retiredResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").WithVersion(3).Retired())
            .Build();

        _registry.GetExplicitSchemaAsync("school-workforce", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(unscheduledResolved));
        _registry.GetExplicitSchemaAsync("school-workforce", 2, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(upcomingResolved));
        _registry.GetExplicitSchemaAsync("school-workforce", 3, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(retiredResolved));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task GetSchemaDetailUseCase_Should_Reject_NonActive_Versions_Without_OptIn(int version)
    {
        // Arrange
        var sut = new GetSchemaDetailUseCase(_registry);
        var request = new GetSchemaDetailRequest("school-workforce", version);

        // Act
        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, true, false, false, "Validated")]
    [InlineData(2, false, true, false, "Scheduled")]
    [InlineData(3, false, false, true, "Retired")]
    public async Task GetSchemaDetailUseCase_Should_Return_NonActive_Version_When_Opted_In(
        int version,
        bool includeUnscheduled,
        bool includeUpcoming,
        bool includeRetired,
        string expectedState)
    {
        // Arrange
        var sut = new GetSchemaDetailUseCase(_registry);
        var request = new GetSchemaDetailRequest(
            "school-workforce",
            version,
            IncludeUnscheduled: includeUnscheduled,
            IncludeUpcoming: includeUpcoming,
            IncludeRetired: includeRetired);

        // Act
        var detail = (await sut.ExecuteAsync(request, CancellationToken.None)).ShouldSucceed();

        // Assert
        detail.State.Name.Should().Be(expectedState);
    }

    [Fact]
    public async Task GetSchemaDetailUseCase_Should_Map_Persisted_EngineTraces_And_RegisteredEngines()
    {
        // Arrange
        var validationService = Substitute.For<IPayloadValidationService>();
        validationService.GetRegisteredEngines().Returns(
        [
            new ValidationEngineDescriptor("json-schema", "JsonSchema.Net Validator", ValidationEngineKinds.StructuralSchema, "Validates JSON Schema"),
            new ValidationEngineDescriptor("cel", "Google CEL Engine", ValidationEngineKinds.QualityRule, "Evaluates CEL rules")
        ]);

        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce"))
            .Build();

        resolved.Metadata.RecordTestRun(
            passed: true,
            appVersion: "2026.11.1",
            engineTraces:
            [
                new SchemaEngineTraceRecord(
                    EngineId: "json-schema",
                    DisplayName: "JsonSchema.Net Validator",
                    Kind: ValidationEngineKinds.StructuralSchema,
                    Description: "Validates JSON Schema",
                    Outcome: "Succeeded",
                    ItemsEvaluated: 2,
                    ViolationCount: 0,
                    DurationMs: 5,
                    Summary: "Schema valid")
            ]);

        _registry.GetExplicitSchemaAsync("school-workforce", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        var sut = new GetSchemaDetailUseCase(_registry, validationService: validationService);
        var request = new GetSchemaDetailRequest("school-workforce", 1, IncludeUnscheduled: true);

        // Act
        var detail = (await sut.ExecuteAsync(request, CancellationToken.None)).ShouldSucceed();

        // Assert
        detail.RegisteredEngines.Should().HaveCount(2);
        detail.LastTestRunEngineTraces.Should().ContainSingle(t =>
            t.Descriptor.EngineId == "json-schema" && t.ItemsEvaluated == 2 && t.Summary == "Schema valid");
    }

    [Fact]
    public async Task GetValidationEnginesUseCase_Should_Return_All_Registered_Engines()
    {
        // Arrange
        var validationService = Substitute.For<IPayloadValidationService>();
        validationService.GetRegisteredEngines().Returns(
        [
            new ValidationEngineDescriptor("json-schema", "JsonSchema.Net Validator", ValidationEngineKinds.StructuralSchema, "Validates JSON Schema"),
            new ValidationEngineDescriptor("cel", "Google CEL Engine", ValidationEngineKinds.QualityRule, "Evaluates CEL rules")
        ]);
        var sut = new GetValidationEnginesUseCase(validationService);

        // Act
        var result = (await sut.ExecuteAsync(new GetValidationEnginesRequest(), CancellationToken.None)).ShouldSucceed();

        // Assert
        result.Engines.Should().HaveCount(2);
    }
}
