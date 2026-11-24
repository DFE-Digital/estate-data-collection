using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Schema.GetValidationEngines;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class GetSchemasAndDetailUseCaseTests
{
    [Fact]
    public async Task GetSchemasUseCase_Should_Expose_Only_Active_Schemas_By_Default_And_Allow_Filtering_Upcoming_Or_Retired()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var unscheduledMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(1).Unscheduled().Build();
        var activeMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(2).Build();
        var deprecatedMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(3).Build();
        deprecatedMeta.Deprecate();
        var upcomingMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(4).ScheduledInFuture(5).Build();
        var retiredMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(5).Retired().Build();

        registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([unscheduledMeta, activeMeta, deprecatedMeta, upcomingMeta, retiredMeta]));

        var useCase = new GetSchemasUseCase(registry);

        var defaultPublic = (await useCase.ExecuteAsync(new GetSchemasRequest(), CancellationToken.None)).ShouldSucceed();
        defaultPublic.Schemas.Should().ContainSingle();
        defaultPublic.Schemas[0].Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 3]);

        var withUpcoming = (await useCase.ExecuteAsync(new GetSchemasRequest(IncludeUpcoming: true), CancellationToken.None)).ShouldSucceed();
        withUpcoming.Schemas[0].Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 3, 4]);

        var withRetired = (await useCase.ExecuteAsync(new GetSchemasRequest(IncludeRetired: true), CancellationToken.None)).ShouldSucceed();
        withRetired.Schemas[0].Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 3, 5]);

        var withUpcomingAndRetired = (await useCase.ExecuteAsync(
            new GetSchemasRequest(IncludeUpcoming: true, IncludeRetired: true), CancellationToken.None)).ShouldSucceed();
        withUpcomingAndRetired.Schemas[0].Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 3, 4, 5]);

        var adminResult = (await useCase.ExecuteAsync(
            new GetSchemasRequest(IncludeUnscheduled: true, IncludeUpcoming: true, IncludeRetired: true), CancellationToken.None)).ShouldSucceed();
        adminResult.Schemas[0].Versions.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetSchemasUseCase_Should_Map_LastTestRun_Telemetry_Including_Warnings()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var meta = new SchemaMetadataBuilder().WithName("cdc_survey").WithVersion(1).Build();
        meta.RecordTestRun(
            passed: true,
            appVersion: "2026.11.1+sha",
            errors: [],
            warnings: ["SampleInvalidFixtures:0 failed validation, but unknown if expected."]);

        registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([meta]));

        var useCase = new GetSchemasUseCase(registry);

        var result = (await useCase.ExecuteAsync(new GetSchemasRequest(IncludeUnscheduled: true), CancellationToken.None)).ShouldSucceed();
        var summary = result.Schemas.Should().ContainSingle().Subject.Versions.Should().ContainSingle().Subject;
        summary.LastTestRunAtUtc.Should().NotBeNull();
        summary.LastTestRunAppVersion.Should().Be("2026.11.1+sha");
        summary.LastTestRunPassed.Should().BeTrue();
        summary.LastTestRunWarnings.Should().ContainSingle()
            .Which.Should().Contain("SampleInvalidFixtures:0");
    }

    [Fact]
    public async Task GetSchemaDetailUseCase_Should_Enforce_Default_Active_Only_With_Upcoming_And_Retired_OptIn()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var unscheduledResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").WithVersion(1).Unscheduled())
            .Build();
        var upcomingResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").WithVersion(2).ScheduledInFuture(3))
            .Build();
        var retiredResolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").WithVersion(3).Retired())
            .Build();

        registry.GetExplicitSchemaAsync("school-workforce", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(unscheduledResolved));
        registry.GetExplicitSchemaAsync("school-workforce", 2, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(upcomingResolved));
        registry.GetExplicitSchemaAsync("school-workforce", 3, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(retiredResolved));

        var useCase = new GetSchemaDetailUseCase(registry);

        (await useCase.ExecuteAsync(new GetSchemaDetailRequest("school-workforce", 1), CancellationToken.None))
            .IsFailure.Should().BeTrue();

        var adminUnscheduled = (await useCase.ExecuteAsync(
            new GetSchemaDetailRequest("school-workforce", 1, IncludeUnscheduled: true),
            CancellationToken.None)).ShouldSucceed();
        adminUnscheduled.State.Name.Should().Be("Validated");
        adminUnscheduled.EffectiveFromUtc.Should().BeNull();

        (await useCase.ExecuteAsync(new GetSchemaDetailRequest("school-workforce", 2), CancellationToken.None))
            .IsFailure.Should().BeTrue();
        (await useCase.ExecuteAsync(new GetSchemaDetailRequest("school-workforce", 2, IncludeUpcoming: true), CancellationToken.None))
            .ShouldSucceed().State.Name.Should().Be("Scheduled");

        (await useCase.ExecuteAsync(new GetSchemaDetailRequest("school-workforce", 3), CancellationToken.None))
            .IsFailure.Should().BeTrue();
        (await useCase.ExecuteAsync(new GetSchemaDetailRequest("school-workforce", 3, IncludeRetired: true), CancellationToken.None))
            .ShouldSucceed().State.Name.Should().Be("Retired");
    }

    [Fact]
    public async Task GetSchemaDetailUseCase_Should_Map_Persisted_EngineTraces_And_RegisteredEngines()
    {
        var registry = Substitute.For<ISchemaRegistry>();
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

        registry.GetExplicitSchemaAsync("school-workforce", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        var detail = (await new GetSchemaDetailUseCase(registry, validationService: validationService)
            .ExecuteAsync(new GetSchemaDetailRequest("school-workforce", 1, IncludeUnscheduled: true), CancellationToken.None))
            .ShouldSucceed();

        detail.RegisteredEngines.Should().HaveCount(2);
        detail.LastTestRunEngineTraces.Should().ContainSingle(t =>
            t.Descriptor.EngineId == "json-schema" && t.ItemsEvaluated == 2 && t.Summary == "Schema valid");

        var enginesResult = (await new GetValidationEnginesUseCase(validationService)
            .ExecuteAsync(new GetValidationEnginesRequest(), CancellationToken.None))
            .ShouldSucceed();
        enginesResult.Engines.Should().HaveCount(2);
    }
}
