using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Json.Schema;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class RunSchemaTestsUseCaseTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaVerificationService _verifier = Substitute.For<ISchemaVerificationService>();
    private readonly RunSchemaTestsUseCase _sut;

    public RunSchemaTestsUseCaseTests()
    {
        _sut = new RunSchemaTestsUseCase(_registry, _verifier);
    }

    [Fact]
    public async Task When_Running_Single_Schema_Tests_Should_Verify_Schema_Rules_Fixtures_And_Record_Audit_Metadata()
    {
        // Arrange
        var metadata = SchemaMetadata.CreateDraft(
            "school-site",
            1,
            JsonSchemaFixtures.ValidV1Schema,
            new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true });
        metadata.ScheduleActivation(DateTimeOffset.UtcNow.AddDays(-1));

        var resolved = new ResolvedSchema(
            Metadata: metadata,
            Schema: JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema),
            Rules: [new BusinessRule { Id = "capacity-positive", Engine = "cel", Description = "Capacity must be >= 0" }],
            RawRulesJson: """[{"id":"capacity-positive","engine":"cel","description":"Capacity must be >= 0","implementation":"capacity >= 0"}]""",
            ValidFixtures: ["""{"urn":123456,"capacity":100}"""],
            InvalidFixtures: ["""{"urn":123456,"capacity":-1}"""]);

        _registry.GetExplicitSchemaAsync("school-site", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        ValidationEngineTrace[] traces =
        [
            new(
                new WorkflowEngineDescriptor("cel", "Google CEL Engine", ValidationEngineKinds.QualityRule, "CEL expressions"),
                "Succeeded",
                ItemsEvaluated: 3,
                ViolationCount: 0,
                DurationMs: 4,
                Summary: "1 rule(s) verified; checked 1 valid and 1 invalid fixture(s)")
        ];

        _verifier.VerifyCandidateSchemaAsync(
                metadata.RawJsonSchema,
                resolved.RawRulesJson,
                Arg.Is<IReadOnlyList<string>>(v => v.Count == 1),
                Arg.Is<IReadOnlyList<string>>(inv => inv.Count == 1),
                metadata.IdentifierConfig,
                Arg.Any<CancellationToken>(),
                Arg.Any<SchemaValidationPolicy?>(),
                Arg.Any<IReadOnlyDictionary<string, JsonNode?>?>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [], EngineTraces: traces)));

        _registry.UpdateSchemaMetadataAsync(metadata, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new RunSchemaTestsRequest("school-site", 1);

        // Act
        var result = (await _sut.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        result.TotalSchemasChecked.Should().Be(1);
        result.PassedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.AppVersion.Should().Be(AppVersionProvider.InformativeVersion);

        var item = result.Results.Should().ContainSingle().Subject;
        item.Passed.Should().BeTrue();
        item.ValidFixtureCount.Should().Be(1);
        item.InvalidFixtureCount.Should().Be(1);
        item.BusinessRuleCount.Should().Be(1);
        item.EngineTraces.Should().ContainSingle(t => t.Descriptor.EngineId == "cel");

        metadata.LastTestRunPassed.Should().BeTrue();
        metadata.LastTestRunAppVersion.Should().Be(AppVersionProvider.InformativeVersion);
        metadata.LastTestRunAtUtc.Should().NotBeNull();
        metadata.LastTestRunErrors.Should().BeEmpty();
        metadata.LastTestRunEngineTraces.Should().ContainSingle(t =>
            t.EngineId == "cel" && t.Outcome == "Succeeded" && t.ItemsEvaluated == 3);

        await _registry.Received(1).UpdateSchemaMetadataAsync(metadata, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Running_All_Schemas_Should_Skip_Retired_By_Default_And_Surface_Regression_Failures()
    {
        // Arrange
        var activeMeta = SchemaMetadata.CreateDraft("active-schema", 1, JsonSchemaFixtures.ValidV1Schema);
        activeMeta.ScheduleActivation(DateTimeOffset.UtcNow.AddDays(-1));

        var retiredMeta = SchemaMetadata.CreateDraft("retired-schema", 1, JsonSchemaFixtures.ValidV1Schema);
        retiredMeta.Retire();

        _registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([activeMeta, retiredMeta]));

        var resolvedActive = new ResolvedSchema(
            Metadata: activeMeta,
            Schema: JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema),
            ValidFixtures: ["""{"urn":123456}"""]);

        _registry.GetExplicitSchemaAsync("active-schema", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolvedActive));

        _verifier.VerifyCandidateSchemaAsync(
                activeMeta.RawJsonSchema,
                null,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<SchemaIdentifierConfig?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<SchemaValidationPolicy?>(),
                Arg.Any<IReadOnlyDictionary<string, JsonNode?>?>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult(
                [],
                [new ValidationMessage(typeof(RunSchemaTestsUseCaseTests), "SampleValidFixtures:0", "SampleValidFixtures:0 violated quality rules: capacity-positive")])));

        _registry.UpdateSchemaMetadataAsync(activeMeta, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new RunSchemaTestsRequest();

        // Act
        var result = (await _sut.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        result.TotalSchemasChecked.Should().Be(1);
        result.PassedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Results.Should().ContainSingle(r =>
            r.SchemaName == "active-schema" &&
            !r.Passed &&
            r.Errors.Any(e => e.Contains("violated quality rules", StringComparison.Ordinal)));

        activeMeta.LastTestRunPassed.Should().BeFalse();
        activeMeta.LastTestRunErrors.Should().ContainSingle(e => e.Contains("violated quality rules", StringComparison.Ordinal));
        await _registry.DidNotReceive().GetExplicitSchemaAsync("retired-schema", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Single_Schema_Version_Is_Missing_Should_Return_NotFound()
    {
        // Arrange
        _registry.GetExplicitSchemaAsync("missing-schema", 9, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Failure(Error.NotFound(ErrorCodes.SchemaNotFound)));

        var request = new RunSchemaTestsRequest("missing-schema", 9);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.ShouldFailWith("Schema.NotFound");
    }
}
