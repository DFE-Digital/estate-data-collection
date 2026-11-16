using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class IngestDraftSchemaUseCaseTests
{
    [Fact]
    public async Task When_OdcsYaml_Dropped_In_DraftContainer_Should_Parse_Promote_And_Delete_Draft()
    {
        var odcsYaml = CdeContractFixtureLoader.LoadFile("contracts/edc-school-site-jsonlogic.odcs.yaml");
        const string blobPath = "edc-school-site-jsonlogic.odcs.yaml";

        var artifact = new DraftSchemaArtifact(
            TriggerBlobPath: blobPath,
            SchemaName: null,
            Version: null,
            RawSchemaJson: null,
            RawRulesJson: null,
            RawOdcsYaml: odcsYaml,
            SampleValidFixtures: [CdePayloadFixtures.ValidSchoolSitePayload],
            SampleInvalidFixtures: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload],
            ConsumedDraftBlobPaths: [blobPath]);

        var registry = Substitute.For<ISchemaRegistry>();
        registry.GetDraftArtifactAsync(blobPath, Arg.Any<CancellationToken>())
            .Returns(Result<DraftSchemaArtifact>.Success(artifact));
        registry.VerifyCanditateSchemaAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [])));
        registry.SaveSchemaAsync(
                Arg.Any<SchemaMetadata>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        registry.CompleteDraftIngestionAsync(artifact, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new IngestDraftSchemaUseCase(registry, new OdcsContractParser());

        var result = (await sut.ExecuteAsync(new IngestDraftSchemaRequest(blobPath))).ShouldSucceed();

        result.Promoted.Should().BeTrue();
        result.Status.Should().Be("Promoted");
        result.SchemaName.Should().Be("edc-survey-sites");
        result.Version.Should().Be(1);

        await registry.Received(1).SaveSchemaAsync(
            Arg.Is<SchemaMetadata>(m =>
                m.Name == "edc-survey-sites" &&
                m.Version == 1 &&
                m.State == SchemaLifecycleState.Validated &&
                m.EffectiveFromUtc == null &&
                m.EffectiveToUtc == null &&
                m.IdentifierConfig == null),
            Arg.Is<string?>(r => !string.IsNullOrWhiteSpace(r) && r.Contains("CDC-DQ1-JSONLogic")),
            odcsYaml,
            Arg.Any<CancellationToken>());
        await registry.Received(1).CompleteDraftIngestionAsync(artifact, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Draft_Fails_Verification_Should_Record_ErrorJson_And_Keep_Draft()
    {
        var schemaJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.schema.json");
        var rulesJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.rules.json");
        const string blobPath = "edc-school-site/v2.schema.json";

        var artifact = new DraftSchemaArtifact(
            TriggerBlobPath: blobPath,
            SchemaName: "edc-school-site",
            Version: 2,
            RawSchemaJson: schemaJson,
            RawRulesJson: rulesJson,
            RawOdcsYaml: null,
            SampleValidFixtures: [CdePayloadFixtures.InvalidTimestampSchoolSitePayload],
            SampleInvalidFixtures: [],
            ConsumedDraftBlobPaths: [blobPath, "edc-school-site/v2.rules.json"]);

        var registry = Substitute.For<ISchemaRegistry>();
        registry.GetDraftArtifactAsync(blobPath, Arg.Any<CancellationToken>())
            .Returns(Result<DraftSchemaArtifact>.Success(artifact));
        registry.VerifyCanditateSchemaAsync(
                schemaJson,
                rulesJson,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(
                new SchemaVerificationResult([], ["SampleValidFixtures:0 violated quality rules: /survey"])));
        registry.RecordDraftIngestionFailureAsync(
                blobPath,
                "edc-school-site",
                2,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new IngestDraftSchemaUseCase(registry, new OdcsContractParser());

        var result = await sut.ExecuteAsync(new IngestDraftSchemaRequest(blobPath));

        result.IsFailure.Should().BeTrue();
        await registry.Received(1).RecordDraftIngestionFailureAsync(
            blobPath,
            "edc-school-site",
            2,
            Arg.Is<IReadOnlyList<string>>(errs => errs.Count == 1 && errs[0].Contains("/survey")),
            Arg.Any<CancellationToken>());
        await registry.DidNotReceiveWithAnyArgs().SaveSchemaAsync(default!, default, default, default);
        await registry.DidNotReceiveWithAnyArgs().CompleteDraftIngestionAsync(default!, default);
    }
}
