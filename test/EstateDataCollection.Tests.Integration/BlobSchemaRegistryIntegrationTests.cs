using System.Text;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Fixtures;
using Xunit;

namespace EstateDataCollection.Tests.Integration;

[Collection("AzuriteCollection")]
public sealed class BlobSchemaRegistryIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ExistsAsync_Before_Save_Should_Return_False()
    {
        // Arrange
        var schemaRegistry = _harness.Get<ISchemaRegistry>();
        var metadata = new SchemaMetadataBuilder()
            .WithUniqueName("integration_schema")
            .WithVersion(1)
            .WithSchema(JsonSchemaFixtures.ValidV1Schema)
            .Build();

        // Act
        var existsBefore = (await schemaRegistry.ExistsAsync(metadata.Name, metadata.Version)).ShouldSucceed();

        // Assert
        existsBefore.Should().BeFalse();
    }

    [Fact]
    public async Task SaveSchemaAsync_Should_Persist_And_Roundtrip_Metadata_And_Schema()
    {
        // Arrange
        var schemaRegistry = _harness.Get<ISchemaRegistry>();
        var metadata = new SchemaMetadataBuilder()
            .WithUniqueName("integration_schema")
            .WithVersion(1)
            .WithSchema(JsonSchemaFixtures.ValidV1Schema)
            .Build();

        // Act
        (await schemaRegistry.SaveSchemaAsync(metadata)).ShouldSucceed();
        var existsAfter = (await schemaRegistry.ExistsAsync(metadata.Name, metadata.Version)).ShouldSucceed();
        var retrieved = (await schemaRegistry.GetExplicitSchemaAsync(metadata.Name, metadata.Version)).ShouldSucceed();

        // Assert
        existsAfter.Should().BeTrue();
        retrieved.Metadata.Name.Should().Be(metadata.Name);
        retrieved.Metadata.Version.Should().Be(1);
        retrieved.Schema.Should().NotBeNull();
    }

    [Fact]
    public async Task GetExplicitSchemaAsync_When_Missing_Should_Return_SchemaNotFound()
    {
        // Arrange
        var schemaRegistry = _harness.Get<ISchemaRegistry>();

        // Act
        var result = await schemaRegistry.GetExplicitSchemaAsync("definitely_not_here", 999);

        // Assert
        result.ShouldFailWith("Schema.NotFound");
    }

    [Fact]
    public async Task IngestDraftSchema_When_OdcsYaml_Uploaded_Should_Promote_Rules_And_Delete_Draft()
    {
        // Arrange
        var schemaRegistry = _harness.Get<ISchemaRegistry>();
        var schemaName = IntegrationEndpointHarness.UniqueName("edc-sites");
        var draftBlobPath = $"{schemaName}/v1.odcs.yaml";
        const string odcsYaml = EdcSchoolSiteContractBuilder.DefaultOdcsYaml;

        var draftContainer = fixture.Client.GetBlobContainerClient(fixture.DraftSchemasContainerName);
        await draftContainer.GetBlobClient(draftBlobPath)
            .UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(odcsYaml)), overwrite: true);

        var ingestUseCase = _harness.Get<IUseCase<IngestDraftSchemaResult, IngestDraftSchemaRequest>>();

        // Act
        var ingest = (await ingestUseCase.ExecuteAsync(new IngestDraftSchemaRequest(draftBlobPath))).ShouldSucceed();
        var draftStillExists = await draftContainer.GetBlobClient(draftBlobPath).ExistsAsync();
        var resolved = (await schemaRegistry.GetExplicitSchemaAsync(schemaName, 1)).ShouldSucceed();

        // Assert
        ingest.Promoted.Should().BeTrue();
        ingest.SchemaName.Should().Be(schemaName);
        ingest.Version.Should().Be(1);
        draftStillExists.Value.Should().BeFalse();
        resolved.Rules.Should().ContainSingle(r => r.Id == "CDC-DQ1-JSONLogic");
    }

    [Fact]
    public async Task AzuriteFixture_Should_Use_Unique_Test_Containers_And_Delete_Them_On_Dispose()
    {
        // Arrange
        var ephemeral = new AzuriteFixture();
        await ephemeral.InitializeAsync();
        var isolatedNames = ephemeral.ContainerNames;
        var tempBlob = ephemeral.Client.GetBlobContainerClient(ephemeral.SchemasContainerName)
            .GetBlobClient("cleanup-check/v1.schema.json");
        await tempBlob.UploadAsync(BinaryData.FromString(JsonSchemaFixtures.ValidV1Schema), overwrite: true);

        // Act
        await ephemeral.DisposeAsync();

        // Assert
        isolatedNames.Should().AllSatisfy(name => AzuriteFixture.IsTestContainerName(name).Should().BeTrue());
        isolatedNames.Should().NotContain(["schemas", "draft-schemas", "payloads", "processed"]);
        foreach (var name in isolatedNames)
        {
            (await fixture.Client.GetBlobContainerClient(name).ExistsAsync()).Value.Should().BeFalse();
        }
    }

    [Fact]
    public async Task UpdateSchemaFixturesAsync_Should_Replace_Invalidate_Cache_And_Allow_Clearing()
    {
        // Arrange
        var schemaRegistry = _harness.Get<ISchemaRegistry>();
        var metadata = new SchemaMetadataBuilder()
            .WithUniqueName("fixture_update_schema")
            .WithVersion(1)
            .WithSchema(JsonSchemaFixtures.ValidV1Schema)
            .Build();

        (await schemaRegistry.SaveSchemaAsync(
            metadata,
            rawRulesJson: null,
            rawOdcsYaml: null,
            sampleValidFixtures: ["""{"urn":100001,"schoolName":"Initial School"}"""],
            sampleInvalidFixtures: ["""{"urn":"bad"}"""])).ShouldSucceed();

        _ = (await schemaRegistry.GetExplicitSchemaAsync(metadata.Name, 1)).ShouldSucceed();

        // Act
        (await schemaRegistry.UpdateSchemaFixturesAsync(
            metadata.Name,
            1,
            ["""{"urn":100001,"schoolName":"Updated A"}""", """{"urn":100002,"schoolName":"Updated B"}"""],
            [])).ShouldSucceed();

        var updated = (await schemaRegistry.GetExplicitSchemaAsync(metadata.Name, 1)).ShouldSucceed();

        // Assert
        updated.ValidFixtures.Should().HaveCount(2);
        updated.ValidFixtures![1].Should().Contain("Updated B");
        updated.InvalidFixtures.Should().BeEmpty();
    }
}
