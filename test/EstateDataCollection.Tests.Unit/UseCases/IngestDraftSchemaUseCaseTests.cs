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
        var verifier = Substitute.For<ISchemaVerificationService>();
        registry.GetDraftArtifactAsync(blobPath, Arg.Any<CancellationToken>())
            .Returns(Result<DraftSchemaArtifact>.Success(artifact));
        verifier.VerifyCandidateSchemaAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<SchemaIdentifierConfig?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(new SchemaVerificationResult([], [])));
        registry.SaveSchemaAsync(
                Arg.Any<SchemaMetadata>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        registry.CompleteDraftIngestionAsync(artifact, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new IngestDraftSchemaUseCase(registry, verifier, new OdcsContractParser());

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
            Arg.Is<IReadOnlyList<string>?>(v => v != null && v.Count == 1),
            Arg.Is<IReadOnlyList<string>?>(inv => inv != null && inv.Count == 1),
            Arg.Any<CancellationToken>());
        await registry.Received(1).CompleteDraftIngestionAsync(artifact, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Draft_Includes_MetaJson_IdentifierConfig_Should_Verify_And_Persist_ResolvedConfig()
    {
        const string schemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["establishment"],
          "properties": {
            "establishment": {
              "type": "object",
              "required": ["laestab"],
              "properties": { "laestab": { "type": "string" } }
            }
          }
        }
        """;
        const string blobPath = "edc-laestab/v1.schema.json";
        var metaIdentifier = new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab" };
        var resolvedIdentifier = metaIdentifier with { Required = true };

        var artifact = new DraftSchemaArtifact(
            TriggerBlobPath: blobPath,
            SchemaName: "edc-laestab",
            Version: 1,
            RawSchemaJson: schemaJson,
            RawRulesJson: null,
            RawOdcsYaml: null,
            SampleValidFixtures: [],
            SampleInvalidFixtures: [],
            ConsumedDraftBlobPaths: [blobPath, "edc-laestab/v1.meta.json"],
            IdentifierConfig: metaIdentifier);

        var registry = Substitute.For<ISchemaRegistry>();
        var verifier = Substitute.For<ISchemaVerificationService>();
        registry.GetDraftArtifactAsync(blobPath, Arg.Any<CancellationToken>())
            .Returns(Result<DraftSchemaArtifact>.Success(artifact));
        verifier.VerifyCandidateSchemaAsync(
                schemaJson,
                null,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                metaIdentifier,
                Arg.Any<CancellationToken>())
            .Returns(Result<SchemaVerificationResult>.Success(
                new SchemaVerificationResult([], [], ResolvedIdentifierConfig: resolvedIdentifier)));
        registry.SaveSchemaAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        registry.CompleteDraftIngestionAsync(artifact, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new IngestDraftSchemaUseCase(registry, verifier, new OdcsContractParser());

        var result = (await sut.ExecuteAsync(new IngestDraftSchemaRequest(blobPath))).ShouldSucceed();

        result.Promoted.Should().BeTrue();
        await registry.Received(1).SaveSchemaAsync(
            Arg.Is<SchemaMetadata>(m =>
                m.IdentifierConfig != null &&
                m.IdentifierConfig.Type == "LAEstab" &&
                m.IdentifierConfig.JsonPointer == "/establishment/laestab" &&
                m.IdentifierConfig.Required == true),
            Arg.Any<CancellationToken>());
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
        var verifier = Substitute.For<ISchemaVerificationService>();
        registry.GetDraftArtifactAsync(blobPath, Arg.Any<CancellationToken>())
            .Returns(Result<DraftSchemaArtifact>.Success(artifact));
        verifier.VerifyCandidateSchemaAsync(
                schemaJson,
                rulesJson,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<SchemaIdentifierConfig?>(),
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

        var sut = new IngestDraftSchemaUseCase(registry, verifier, new OdcsContractParser());

        var result = await sut.ExecuteAsync(new IngestDraftSchemaRequest(blobPath));

        result.IsFailure.Should().BeTrue();
        await registry.Received(1).RecordDraftIngestionFailureAsync(
            blobPath,
            "edc-school-site",
            2,
            Arg.Is<IReadOnlyList<string>>(errs => errs.Count == 1 && errs[0].Contains("/survey")),
            Arg.Any<CancellationToken>());
        await registry.DidNotReceiveWithAnyArgs().SaveSchemaAsync(default!, default, default, default);
        await registry.DidNotReceiveWithAnyArgs().SaveSchemaAsync(default!, default, default, default, default, default);
        await registry.DidNotReceiveWithAnyArgs().CompleteDraftIngestionAsync(default!, default);
    }
}
