using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;
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

public sealed class UpdateSchemaTestsUseCaseTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly UpdateSchemaTestsUseCase _sut;

    public UpdateSchemaTestsUseCaseTests()
    {
        _sut = new UpdateSchemaTestsUseCase(_registry);
    }

    [Fact]
    public async Task When_Fixtures_Pass_Verification_Should_Update_Blobs_And_Metadata()
    {
        var metadata = SchemaMetadata.CreateDraft(
            "school-site",
            1,
            JsonSchemaFixtures.ValidV1Schema,
            new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true });
        metadata.ScheduleActivation(DateTimeOffset.UtcNow.AddDays(-1));

        var resolved = new ResolvedSchema(
            Metadata: metadata,
            Schema: JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema),
            Rules: [new BusinessRule { Id = "capacity-positive", Engine = "jsonlogic", Description = "Capacity >= 0" }],
            RawRulesJson: """[{"id":"capacity-positive","description":"Capacity >= 0","severity":"ERROR","path":"/","message":"Capacity >= 0","assert":{">=":[{"var":"capacity"},0]}}]""",
            ValidFixtures: ["""{"urn":123456,"capacity":10}"""],
            InvalidFixtures: []);

        _registry.GetExplicitSchemaAsync("school-site", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        IReadOnlyList<string> newValid = ["""{"urn":123456,"capacity":50}""", """{"urn":654321,"capacity":75}"""];
        IReadOnlyList<string> newInvalid = ["""{"urn":123456,"capacity":-1}"""];

        _registry.VerifyCanditateSchemaAsync(
                metadata.RawJsonSchema,
                resolved.RawRulesJson,
                Arg.Is<IReadOnlyList<string>>(v => v.Count == 2),
                Arg.Is<IReadOnlyList<string>>(inv => inv.Count == 1),
                metadata.IdentifierConfig,
                Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [])));

        _registry.UpdateSchemaFixturesAsync(
                "school-site",
                1,
                Arg.Is<IReadOnlyList<string>>(v => v.Count == 2),
                Arg.Is<IReadOnlyList<string>>(inv => inv.Count == 1),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _registry.UpdateSchemaMetadataAsync(metadata, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = (await _sut.ExecuteAsync(new UpdateSchemaTestsRequest(
            "school-site",
            1,
            newValid,
            newInvalid))).ShouldSucceed();

        result.SchemaName.Should().Be("school-site");
        result.Version.Should().Be(1);
        result.ValidFixtureCount.Should().Be(2);
        result.InvalidFixtureCount.Should().Be(1);
        result.BusinessRuleCount.Should().Be(1);
        result.AppVersion.Should().Be(AppVersionProvider.InformativeVersion);

        metadata.LastTestRunPassed.Should().BeTrue();
        metadata.LastTestRunAppVersion.Should().Be(AppVersionProvider.InformativeVersion);
        metadata.LastTestRunAtUtc.Should().NotBeNull();

        await _registry.Received(1).UpdateSchemaFixturesAsync(
            "school-site",
            1,
            Arg.Is<IReadOnlyList<string>>(v => v.Count == 2),
            Arg.Is<IReadOnlyList<string>>(inv => inv.Count == 1),
            Arg.Any<CancellationToken>());
        await _registry.Received(1).UpdateSchemaMetadataAsync(metadata, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Fixtures_Fail_Verification_Should_Return_ValidationError_And_Not_Overwrite_Blobs()
    {
        var metadata = SchemaMetadata.CreateDraft("school-site", 1, JsonSchemaFixtures.ValidV1Schema);
        var resolved = new ResolvedSchema(
            Metadata: metadata,
            Schema: JsonSchema.FromText(JsonSchemaFixtures.ValidV1Schema));

        _registry.GetExplicitSchemaAsync("school-site", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        _registry.VerifyCanditateSchemaAsync(
                metadata.RawJsonSchema,
                null,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult(
                [],
                [new ValidationMessage(typeof(UpdateSchemaTestsUseCaseTests), "SampleValidFixtures:0", "SampleValidFixtures:0 violated quality rules: capacity >= 0")])));

        var result = await _sut.ExecuteAsync(new UpdateSchemaTestsRequest(
            "school-site",
            1,
            ValidFixtures: ["""{"urn":123456,"capacity":-5}"""],
            InvalidFixtures: []));

        var err = result.ShouldFailWith("Schema.VerificationFailed");
        err.Type.Should().Be(ErrorType.Validation);
        err.ValidationErrors.Should().ContainSingle(v =>
            v.PropertyPath == "SampleValidFixtures:0" &&
            v.Message.Contains("violated quality rules", StringComparison.Ordinal));

        await _registry.DidNotReceiveWithAnyArgs().UpdateSchemaFixturesAsync(default!, default, default!, default!, default);
        await _registry.DidNotReceiveWithAnyArgs().UpdateSchemaMetadataAsync(default!, default);
    }

    [Fact]
    public async Task When_Schema_Does_Not_Exist_Should_Return_NotFound()
    {
        _registry.GetExplicitSchemaAsync("missing-schema", 4, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Failure(Error.NotFound(ErrorCodes.SchemaNotFound)));

        var result = await _sut.ExecuteAsync(new UpdateSchemaTestsRequest("missing-schema", 4, [], []));

        result.ShouldFailWith("Schema.NotFound");
    }
}
