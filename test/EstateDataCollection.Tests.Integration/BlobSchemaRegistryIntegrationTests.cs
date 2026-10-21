using Azure.Storage.Blobs;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Fixtures;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace EstateDataCollection.Tests.Integration;

[Collection("AzuriteCollection")]
public class BlobSchemaRegistryIntegrationTests(AzuriteFixture fixture)
{
    private readonly BlobSchemaRegistry _sut = new(
        fixture.Client,
        Options.Create(new BlobSchemaRegistryOptions
        {
            ContainerName = "schemas",
            CacheKey = "schema:{schemaName}:v{version}",
            SchemaBlobDirectory = "{schemaName}/v{version}.schema.json",
            MetaBlobDirectory = "{schemaName}/v{version}.meta.json"
        }),
        new MemoryCache(new MemoryCacheOptions())
    );

    [Fact]
    public async Task When_Saving_A_Schema_To_Azurite_It_Should_Roundtrip_Successfully_And_Be_Retrievable()
    {
        // Arrange
        var metadata = new SchemaMetadataBuilder()
            .WithName("integration_schema_" + Guid.NewGuid().ToString("N"))
            .WithVersion(1)
            .WithSchema(JsonSchemaFixtures.ValidV1Schema)
            .Build();

        // 1. Check Exists before save -> should be false
        var existsBefore = await _sut.ExistsAsync(metadata.Name, metadata.Version);
        existsBefore.IsSuccess.Should().BeTrue();
        existsBefore.Value.Should().BeFalse();

        // 2. Save schema + metadata to Azurite
        var saveResult = await _sut.SaveSchemaAsync(metadata);
        saveResult.IsSuccess.Should().BeTrue();

        // 3. Check Exists after save -> should be true
        var existsAfter = await _sut.ExistsAsync(metadata.Name, metadata.Version);
        existsAfter.IsSuccess.Should().BeTrue();
        existsAfter.Value.Should().BeTrue();

        // 4. Retrieve explicit schema
        var getResult = await _sut.GetExplicitSchemaAsync(metadata.Name, metadata.Version);
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Metadata.Name.Should().Be(metadata.Name);
        getResult.Value.Metadata.Version.Should().Be(1);
        getResult.Value.Schema.Should().NotBeNull();
    }

    [Fact]
    public async Task When_Requesting_A_Non_Existent_Schema_From_Azurite_It_Should_Return_Not_Found()
    {
        // Act
        var result = await _sut.GetExplicitSchemaAsync("definitely_not_here", 999);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.NotFound");
    }
}
