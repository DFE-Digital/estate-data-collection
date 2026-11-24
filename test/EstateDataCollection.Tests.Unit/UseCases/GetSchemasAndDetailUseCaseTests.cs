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
    public async Task GetSchemasUseCase_Should_Exclude_Unscheduled_Schemas_When_IncludeUnscheduled_Is_False()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var unscheduledMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(1).Unscheduled().Build();
        var activeMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(2).Build();

        registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([unscheduledMeta, activeMeta]));

        var useCase = new GetSchemasUseCase(registry);

        var publicResult = (await useCase.ExecuteAsync(new GetSchemasRequest(IncludeUnscheduled: false), CancellationToken.None)).ShouldSucceed();
        publicResult.Schemas.Should().ContainSingle();
        publicResult.Schemas[0].Versions.Should().ContainSingle(v => v.Version == 2);

        var adminResult = (await useCase.ExecuteAsync(new GetSchemasRequest(IncludeUnscheduled: true), CancellationToken.None)).ShouldSucceed();
        adminResult.Schemas[0].Versions.Should().HaveCount(2);
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
    public async Task GetSchemaDetailUseCase_Should_Return_NotFound_When_Schema_Is_Unscheduled_And_IncludeUnscheduled_Is_False()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").Unscheduled())
            .Build();

        registry.GetExplicitSchemaAsync("school-workforce", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        var useCase = new GetSchemaDetailUseCase(registry);

        var publicResult = await useCase.ExecuteAsync(
            new GetSchemaDetailRequest("school-workforce", 1, IncludeUnscheduled: false),
            CancellationToken.None);
        publicResult.IsFailure.Should().BeTrue();
        publicResult.Error.Type.Should().Be(ErrorType.NotFound);

        var adminResult = (await useCase.ExecuteAsync(
            new GetSchemaDetailRequest("school-workforce", 1, IncludeUnscheduled: true),
            CancellationToken.None)).ShouldSucceed();
        adminResult.State.Name.Should().Be("Validated");
        adminResult.EffectiveFromUtc.Should().BeNull();
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
