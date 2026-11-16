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
        var draftContainer = _harness.BlobClient.GetBlobContainerClient("draft-schemas");
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
}
