using System.Text;
using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;
using EstateDataCollection.Infrastructure.Registry;
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
        var metadata = new SchemaMetadataBuilder()
            .WithUniqueName("integration_schema")
            .WithVersion(1)
            .WithSchema(JsonSchemaFixtures.ValidV1Schema)
            .Build();

        var existsBefore = (await _harness.SchemaRegistry.ExistsAsync(metadata.Name, metadata.Version)).ShouldSucceed();
        existsBefore.Should().BeFalse();

        (await _harness.SchemaRegistry.SaveSchemaAsync(metadata)).ShouldSucceed();

        var existsAfter = (await _harness.SchemaRegistry.ExistsAsync(metadata.Name, metadata.Version)).ShouldSucceed();
        existsAfter.Should().BeTrue();

        var retrieved = (await _harness.SchemaRegistry.GetExplicitSchemaAsync(metadata.Name, metadata.Version)).ShouldSucceed();
        retrieved.Metadata.Name.Should().Be(metadata.Name);
        retrieved.Metadata.Version.Should().Be(1);
        retrieved.Schema.Should().NotBeNull();
    }

    [Fact]
    public async Task When_Requesting_A_Non_Existent_Schema_From_Azurite_It_Should_Return_Not_Found()
    {
        var result = await _harness.SchemaRegistry.GetExplicitSchemaAsync("definitely_not_here", 999);
        result.ShouldFailWith("Schema.NotFound");
    }

    [Fact]
    public async Task When_OdcsYaml_Uploaded_To_DraftContainer_In_Azurite_It_Should_Ingest_Promote_And_Delete_Draft()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("edc-sites");
        var draftBlobPath = $"{schemaName}/v1.odcs.yaml";
        var odcsYaml = CdeContractFixtureLoader.LoadFile("contracts/edc-school-site-jsonlogic.odcs.yaml");

        var draftContainer = fixture.Client.GetBlobContainerClient("draft-schemas");
        await draftContainer.GetBlobClient(draftBlobPath)
            .UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(odcsYaml)), overwrite: true);

        var ingestUseCase = new IngestDraftSchemaUseCase(_harness.SchemaRegistry, new OdcsContractParser());

        var ingest = (await ingestUseCase.ExecuteAsync(new IngestDraftSchemaRequest(draftBlobPath))).ShouldSucceed();
        ingest.Promoted.Should().BeTrue();
        ingest.SchemaName.Should().Be(schemaName);
        ingest.Version.Should().Be(1);

        var draftStillExists = await draftContainer.GetBlobClient(draftBlobPath).ExistsAsync();
        draftStillExists.Value.Should().BeFalse();

        var resolved = (await _harness.SchemaRegistry.GetExplicitSchemaAsync(schemaName, 1)).ShouldSucceed();
        resolved.Rules.Should().ContainSingle(r => r.Id == "CDC-DQ1-JSONLogic");
    }
}
