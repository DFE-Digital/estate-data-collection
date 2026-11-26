using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class IngestDraftFailureTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaVerificationService _verifier = Substitute.For<ISchemaVerificationService>();
    private readonly IngestDraftSchemaUseCase _sut;

    public IngestDraftFailureTests()
    {
        _sut = new IngestDraftSchemaUseCase(_registry, _verifier, new OdcsContractParser());
    }

    [Fact]
    public async Task When_Draft_Fails_Verification_Should_Persist_Invalid_With_ErrorReport_And_Clean_Up_Draft()
    {
        // Arrange
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rulesJson = EdcSchoolSiteContractBuilder.DefaultRulesJson;
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

        _registry.GetDraftArtefactAsync(blobPath, Arg.Any<CancellationToken>())
            .Returns(Result<DraftSchemaArtifact>.Success(artifact));
        _registry.ExistsAsync("edc-school-site", 2, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));
        _verifier.VerifyCandidateSchemaAsync(
                schemaJson,
                rulesJson,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<SchemaIdentifierConfig?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<SchemaValidationPolicy?>(),
                Arg.Any<IReadOnlyDictionary<string, JsonNode?>?>())
            .Returns(Result<SchemaVerificationResult>.Success(
                new SchemaVerificationResult([], ["SampleValidFixtures:0 violated quality rules: /survey"])));
        _registry.SaveSchemaAsync(
                Arg.Any<SchemaMetadata>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _registry.SaveErrorReportAsync(Arg.Any<SchemaErrorReport>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _registry.CompleteDraftIngestionAsync(artifact, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var request = new IngestDraftSchemaRequest(blobPath);

        // Act
        var result = (await _sut.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        result.Promoted.Should().BeFalse();
        result.Status.Should().Be("Invalid");
        result.State!.Name.Should().Be("Invalid");
        result.HasErrorReport.Should().BeTrue();
        result.Issues.Should().ContainKey(RuleSeverity.Error);

        await _registry.Received(1).SaveSchemaAsync(
            Arg.Is<SchemaMetadata>(m => m.Name == "edc-school-site" && m.Version == 2 && m.State == SchemaLifecycleState.Invalid && m.HasErrorReport),
            rulesJson,
            null,
            Arg.Is<IReadOnlyList<string>?>(v => v != null && v.Count == 1),
            Arg.Is<IReadOnlyList<string>?>(inv => inv != null && inv.Count == 0),
            Arg.Any<CancellationToken>());
        await _registry.Received(1).SaveErrorReportAsync(
            Arg.Is<SchemaErrorReport>(r => r.SchemaName == "edc-school-site" && r.Version == 2 && r.FixtureErrors != null && r.FixtureErrors.Any(e => e.Contains("/survey"))),
            Arg.Any<CancellationToken>());
        await _registry.Received(1).CompleteDraftIngestionAsync(artifact, Arg.Any<CancellationToken>());
    }
}
