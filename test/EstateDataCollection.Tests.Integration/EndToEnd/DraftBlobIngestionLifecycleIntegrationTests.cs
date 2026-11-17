using System.Text;
using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.EndToEnd;

[Collection("AzuriteCollection")]
public sealed class DraftBlobIngestionLifecycleIntegrationTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Fact]
    public async Task Draft_Blob_Triggered_Should_Promote_Unscheduled_Until_Admin_Schedules_It()
    {
        var draftSchemaName = IntegrationEndpointHarness.UniqueName("draft_evt");
        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        await draftContainer.CreateIfNotExistsAsync();

        var blobPath = $"{draftSchemaName}/v1.schema.json";
        await draftContainer.GetBlobClient(blobPath)
            .UploadAsync(BinaryData.FromString(JsonSchemaFixtures.ValidV1Schema), overwrite: true);

        // 1. Trigger Admin Blob Event Function
        using var dummyStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSchemaFixtures.ValidV1Schema));
        var ingest = (await _harness.IngestDraftSchema.HandleAsync(dummyStream, blobPath, CancellationToken.None)).ShouldSucceed();
        ingest.Status.Should().Be("Promoted");

        // 2. Admin GET sees it as Validated with null effective dates and null IdentifierConfig
        var adminDetail = (await _harness.GetAdminSchemaVersionDetail.HandleAsync(
                HttpRequestBuilder.Empty(), draftSchemaName, 1, CancellationToken.None))
            .ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);
        adminDetail.State.Name.Should().Be("Validated");
        adminDetail.EffectiveFromUtc.Should().BeNull();
        adminDetail.EffectiveToUtc.Should().BeNull();
        adminDetail.IdentifierConfig.Should().BeNull();

        // 3. Public GET does NOT expose it yet
        (await _harness.PublicGetSchemaByName.HandleAsync(HttpRequestBuilder.Empty(), draftSchemaName, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status404NotFound);

        // 4. Admin schedules it
        var scheduleDto = new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30));
        (await _harness.ScheduleSchema.HandleAsync(
                HttpRequestBuilder.FromJson(scheduleDto), draftSchemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // 5. Public GET now exposes the active version
        var catalogueEntry = (await _harness.PublicGetSchemaByName.HandleAsync(
                HttpRequestBuilder.Empty(), draftSchemaName, CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);
        catalogueEntry.ActiveVersion.Should().Be(1);
        catalogueEntry.Versions.Should().ContainSingle(v => v.Version == 1 && v.State.Name == "Active");
    }

    [Fact]
    public async Task Draft_Blob_With_Companion_MetaJson_Should_Persist_Custom_LAEstab_Config_And_Clean_Up_MetaJson()
    {
        var draftSchemaName = IntegrationEndpointHarness.UniqueName("draft_meta");
        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        await draftContainer.CreateIfNotExistsAsync();

        const string customSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["establishment", "response"],
          "properties": {
            "establishment": {
              "type": "object",
              "required": ["laestab"],
              "properties": {
                "laestab": { "type": "string" }
              }
            },
            "response": { "type": "string" }
          }
        }
        """;

        const string companionMetaJson = """
        {
          "identifierConfig": {
            "type": "LAEstab",
            "jsonPointer": "/establishment/laestab"
          }
        }
        """;

        var schemaPath = $"{draftSchemaName}/v1.schema.json";
        var metaPath = $"{draftSchemaName}/v1.meta.json";

        await draftContainer.GetBlobClient(schemaPath)
            .UploadAsync(BinaryData.FromString(customSchemaJson), overwrite: true);
        await draftContainer.GetBlobClient(metaPath)
            .UploadAsync(BinaryData.FromString(companionMetaJson), overwrite: true);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(customSchemaJson));
        var ingest = (await _harness.IngestDraftSchema.HandleAsync(stream, schemaPath, CancellationToken.None)).ShouldSucceed();
        ingest.Promoted.Should().BeTrue();
        ingest.Status.Should().Be("Promoted");

        // Admin detail shows derived Required = true and LAEstab config
        var adminDetail = (await _harness.GetAdminSchemaVersionDetail.HandleAsync(
                HttpRequestBuilder.Empty(), draftSchemaName, 1, CancellationToken.None))
            .ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);
        adminDetail.IdentifierConfig.Should().NotBeNull();
        adminDetail.IdentifierConfig!.Type.Should().Be("LAEstab");
        adminDetail.IdentifierConfig.JsonPointer.Should().Be("/establishment/laestab");
        adminDetail.IdentifierConfig.Required.Should().BeTrue();

        // Both draft schema and companion meta.json were cleaned up
        (await draftContainer.GetBlobClient(schemaPath).ExistsAsync()).Value.Should().BeFalse();
        (await draftContainer.GetBlobClient(metaPath).ExistsAsync()).Value.Should().BeFalse();
    }

    [Fact]
    public async Task Draft_Blob_Without_MetaJson_Or_Default_Urn_Path_Should_Fail_Promotion_And_Write_Error_Blob()
    {
        var draftSchemaName = IntegrationEndpointHarness.UniqueName("draft_no_id");
        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        await draftContainer.CreateIfNotExistsAsync();

        const string customPathOnlySchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["custom_code"],
          "properties": {
            "custom_code": { "type": "string" }
          }
        }
        """;

        var schemaPath = $"{draftSchemaName}/v1.schema.json";
        await draftContainer.GetBlobClient(schemaPath)
            .UploadAsync(BinaryData.FromString(customPathOnlySchema), overwrite: true);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(customPathOnlySchema));
        var result = await _harness.IngestDraftSchema.HandleAsync(stream, schemaPath, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.VerificationFailed");

        // Error diagnostic blob written in draft-schemas and schema NOT promoted
        var errorBlob = draftContainer.GetBlobClient($"{schemaPath}.error.json");
        (await errorBlob.ExistsAsync()).Value.Should().BeTrue();
        (await _harness.SchemaRegistry.ExistsAsync(draftSchemaName, 1)).ShouldSucceed().Should().BeFalse();
    }
}
