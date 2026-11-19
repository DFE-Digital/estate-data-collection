using System.Text;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Fixtures;
using Xunit;

namespace EstateDataCollection.Tests.Integration;

[Collection("AzuriteCollection")]
public class BlobSchemaRegistryIntegrationTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Fact]
    public async Task When_Saving_A_Schema_To_Azurite_It_Should_Roundtrip_Successfully_And_Be_Retrievable()
    {
        var schemaRegistry = _harness.Get<ISchemaRegistry>();
        var metadata = new SchemaMetadataBuilder()
            .WithUniqueName("integration_schema")
            .WithVersion(1)
            .WithSchema(JsonSchemaFixtures.ValidV1Schema)
            .Build();

        var existsBefore = (await schemaRegistry.ExistsAsync(metadata.Name, metadata.Version)).ShouldSucceed();
        existsBefore.Should().BeFalse();

        (await schemaRegistry.SaveSchemaAsync(metadata)).ShouldSucceed();

        var existsAfter = (await schemaRegistry.ExistsAsync(metadata.Name, metadata.Version)).ShouldSucceed();
        existsAfter.Should().BeTrue();

        var retrieved = (await schemaRegistry.GetExplicitSchemaAsync(metadata.Name, metadata.Version)).ShouldSucceed();
        retrieved.Metadata.Name.Should().Be(metadata.Name);
        retrieved.Metadata.Version.Should().Be(1);
        retrieved.Schema.Should().NotBeNull();
    }

    [Fact]
    public async Task When_Requesting_A_Non_Existent_Schema_From_Azurite_It_Should_Return_Not_Found()
    {
        var schemaRegistry = _harness.Get<ISchemaRegistry>();
        var result = await schemaRegistry.GetExplicitSchemaAsync("definitely_not_here", 999);
        result.ShouldFailWith("Schema.NotFound");
    }

    [Fact]
    public async Task When_OdcsYaml_Uploaded_To_DraftContainer_In_Azurite_It_Should_Ingest_Promote_And_Delete_Draft()
    {
        var schemaRegistry = _harness.Get<ISchemaRegistry>();
        var schemaName = IntegrationEndpointHarness.UniqueName("edc-sites");
        var draftBlobPath = $"{schemaName}/v1.odcs.yaml";
        var odcsYaml = CdeContractFixtureLoader.LoadFile("contracts/edc-school-site-jsonlogic.odcs.yaml");

        var draftContainer = fixture.Client.GetBlobContainerClient(fixture.DraftSchemasContainerName);
        await draftContainer.GetBlobClient(draftBlobPath)
            .UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(odcsYaml)), overwrite: true);

        var ingestUseCase = _harness.Get<IUseCase<IngestDraftSchemaResult, IngestDraftSchemaRequest>>();

        var ingest = (await ingestUseCase.ExecuteAsync(new IngestDraftSchemaRequest(draftBlobPath))).ShouldSucceed();
        ingest.Promoted.Should().BeTrue();
        ingest.SchemaName.Should().Be(schemaName);
        ingest.Version.Should().Be(1);

        var draftStillExists = await draftContainer.GetBlobClient(draftBlobPath).ExistsAsync();
        draftStillExists.Value.Should().BeFalse();

        var resolved = (await schemaRegistry.GetExplicitSchemaAsync(schemaName, 1)).ShouldSucceed();
        resolved.Rules.Should().ContainSingle(r => r.Id == "CDC-DQ1-JSONLogic");
    }

    [Fact]
    public async Task When_AzuriteFixture_Disposes_Should_Isolate_And_Delete_All_Test_Containers()
    {
        var ephemeral = new AzuriteFixture();
        await ephemeral.InitializeAsync();

        var isolatedNames = ephemeral.ContainerNames;
        isolatedNames.Should().AllSatisfy(name => AzuriteFixture.IsTestContainerName(name).Should().BeTrue());
        isolatedNames.Should().NotContain(["schemas", "draft-schemas", "payloads", "processed"]);

        foreach (var name in isolatedNames)
        {
            (await ephemeral.Client.GetBlobContainerClient(name).ExistsAsync()).Value.Should().BeTrue();
        }

        // Write a blob into the isolated schema container before disposing
        var tempBlob = ephemeral.Client.GetBlobContainerClient(ephemeral.SchemasContainerName).GetBlobClient("cleanup-check/v1.schema.json");
        await tempBlob.UploadAsync(BinaryData.FromString(JsonSchemaFixtures.ValidV1Schema), overwrite: true);

        await ephemeral.DisposeAsync();

        // Check via the active fixture client that all 4 isolated containers were removed
        foreach (var name in isolatedNames)
        {
            (await fixture.Client.GetBlobContainerClient(name).ExistsAsync()).Value.Should().BeFalse();
        }
    }

    [Fact]
    public async Task When_Updating_Schema_Fixtures_In_Azurite_Should_Replace_Invalidate_Cache_And_Allow_Clearing()
    {
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

        // Prime memory cache
        var initial = (await schemaRegistry.GetExplicitSchemaAsync(metadata.Name, 1)).ShouldSucceed();
        initial.ValidFixtures.Should().HaveCount(1);
        initial.InvalidFixtures.Should().HaveCount(1);

        // Replace fixtures
        (await schemaRegistry.UpdateSchemaFixturesAsync(
            metadata.Name,
            1,
            ["""{"urn":100001,"schoolName":"Updated A"}""", """{"urn":100002,"schoolName":"Updated B"}"""],
            [])).ShouldSucceed();

        var updated = (await schemaRegistry.GetExplicitSchemaAsync(metadata.Name, 1)).ShouldSucceed();
        updated.ValidFixtures.Should().HaveCount(2);
        updated.ValidFixtures![1].Should().Contain("Updated B");
        updated.InvalidFixtures.Should().BeEmpty();
    }
}
