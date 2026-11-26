using System.Text;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.AzureFunctions.Admin.Events;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders.Contracts;
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
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Draft_Blob_Promoted_Should_Start_Validated_Unscheduled_And_Hidden_From_Public_Until_Scheduled()
    {
        // Arrange
        var draftSchemaName = IntegrationEndpointHarness.UniqueName("draft_evt");
        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        await draftContainer.CreateIfNotExistsAsync();

        var blobPath = $"{draftSchemaName}/v1.schema.json";
        await draftContainer.GetBlobClient(blobPath)
            .UploadAsync(BinaryData.FromString(JsonSchemaFixtures.ValidV1Schema), overwrite: true);

        using var dummyStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSchemaFixtures.ValidV1Schema));

        // Act
        var ingest = (await _harness.Get<IngestDraftSchemaEvent>().HandleAsync(dummyStream, blobPath, CancellationToken.None)).ShouldSucceed();
        var adminDetailRes = await _harness.Get<GetAdminSchemaVersionDetailEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), draftSchemaName, 1, CancellationToken.None);
        var publicBeforeSchedule = await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), draftSchemaName, CancellationToken.None);

        // Assert
        ingest.Status.Should().Be("Promoted");
        var adminDetail = adminDetailRes.ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);
        adminDetail.State.Name.Should().Be("Validated");
        adminDetail.EffectiveFromUtc.Should().BeNull();
        adminDetail.EffectiveToUtc.Should().BeNull();
        adminDetail.IdentifierConfig.Should().BeNull();
        publicBeforeSchedule.ShouldHaveStatusCode(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Promoted_Draft_Schema_When_Scheduled_Should_Become_Active_On_Public_Endpoint()
    {
        // Arrange
        var draftSchemaName = IntegrationEndpointHarness.UniqueName("draft_sched");
        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        await draftContainer.CreateIfNotExistsAsync();

        var blobPath = $"{draftSchemaName}/v1.schema.json";
        await draftContainer.GetBlobClient(blobPath)
            .UploadAsync(BinaryData.FromString(JsonSchemaFixtures.ValidV1Schema), overwrite: true);

        using var dummyStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSchemaFixtures.ValidV1Schema));
        (await _harness.Get<IngestDraftSchemaEvent>().HandleAsync(dummyStream, blobPath, CancellationToken.None)).ShouldSucceed();

        var scheduleDto = new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30));

        // Act
        var scheduleRes = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(scheduleDto), draftSchemaName, 1, CancellationToken.None);
        var publicRes = await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), draftSchemaName, CancellationToken.None);

        // Assert
        scheduleRes.ShouldHaveStatusCode(StatusCodes.Status200OK);
        var catalogueEntry = publicRes.ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);
        catalogueEntry.ActiveVersion.Should().Be(1);
        catalogueEntry.Versions.Should().ContainSingle(v => v.Version == 1 && v.State.Name == "Active");
    }

    [Fact]
    public async Task Draft_Blob_Triggered_Via_Yaml_When_Json_Files_Present_Should_Prefer_Json_And_Clean_Up_All_Drafts()
    {
        // Arrange
        var draftSchemaName = IntegrationEndpointHarness.UniqueName("draft_json_wins");
        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        await draftContainer.CreateIfNotExistsAsync();

        const string odcsYaml = EdcSchoolSiteContractBuilder.DefaultOdcsYaml;
        const string overrideSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["urn", "override_field"],
          "properties": {
            "urn": { "type": "integer", "minimum": 1 },
            "override_field": { "type": "string" }
          }
        }
        """;
        const string overrideRulesJson = """
        [
          {
            "id": "JSON-PRECEDENCE-RULE",
            "type": "custom",
            "engine": "cel",
            "description": "Override field must be non-empty",
            "severity": "ERROR",
            "path": "override_field",
            "message": "override_field is required",
            "implementation": "override_field != ''"
          }
        ]
        """;

        var yamlPath = $"{draftSchemaName}/v1.odcs.yaml";
        var schemaPath = $"{draftSchemaName}/v1.schema.json";
        var rulesPath = $"{draftSchemaName}/v1.rules.json";

        await draftContainer.GetBlobClient(schemaPath).UploadAsync(BinaryData.FromString(overrideSchemaJson), overwrite: true);
        await draftContainer.GetBlobClient(rulesPath).UploadAsync(BinaryData.FromString(overrideRulesJson), overwrite: true);
        await draftContainer.GetBlobClient(yamlPath).UploadAsync(BinaryData.FromString(odcsYaml), overwrite: true);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(odcsYaml));

        // Act
        var ingest = (await _harness.Get<IngestDraftSchemaEvent>().HandleAsync(stream, yamlPath, CancellationToken.None)).ShouldSucceed();
        var adminDetailRes = await _harness.Get<GetAdminSchemaVersionDetailEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), draftSchemaName, 1, CancellationToken.None);

        // Assert
        ingest.Promoted.Should().BeTrue();
        ingest.Status.Should().Be("Promoted");

        var adminDetail = adminDetailRes.ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);
        adminDetail.Fields.Should().Contain(f => f.Name == "override_field");
        adminDetail.Fields.Should().NotContain(f => f.Name == "survey");
        adminDetail.QualityRules.Should().ContainSingle(r => r.Id == "JSON-PRECEDENCE-RULE");

        (await draftContainer.GetBlobClient(yamlPath).ExistsAsync()).Value.Should().BeFalse();
        (await draftContainer.GetBlobClient(schemaPath).ExistsAsync()).Value.Should().BeFalse();
        (await draftContainer.GetBlobClient(rulesPath).ExistsAsync()).Value.Should().BeFalse();
    }

    [Fact]
    public async Task Draft_Blob_With_Companion_MetaJson_Should_Persist_LAEstab_Config_And_Clean_Up_MetaJson()
    {
        // Arrange
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

        await draftContainer.GetBlobClient(schemaPath).UploadAsync(BinaryData.FromString(customSchemaJson), overwrite: true);
        await draftContainer.GetBlobClient(metaPath).UploadAsync(BinaryData.FromString(companionMetaJson), overwrite: true);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(customSchemaJson));

        // Act
        var ingest = (await _harness.Get<IngestDraftSchemaEvent>().HandleAsync(stream, schemaPath, CancellationToken.None)).ShouldSucceed();
        var adminDetailRes = await _harness.Get<GetAdminSchemaVersionDetailEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), draftSchemaName, 1, CancellationToken.None);

        // Assert
        ingest.Promoted.Should().BeTrue();
        ingest.Status.Should().Be("Promoted");

        var adminDetail = adminDetailRes.ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);
        adminDetail.IdentifierConfig.Should().NotBeNull();
        adminDetail.IdentifierConfig!.Type.Should().Be("LAEstab");
        adminDetail.IdentifierConfig.JsonPointer.Should().Be("/establishment/laestab");
        adminDetail.IdentifierConfig.Required.Should().BeTrue();

        (await draftContainer.GetBlobClient(schemaPath).ExistsAsync()).Value.Should().BeFalse();
        (await draftContainer.GetBlobClient(metaPath).ExistsAsync()).Value.Should().BeFalse();
    }

    [Fact]
    public async Task Draft_Blob_Without_MetaJson_Or_Default_Urn_Path_Should_Persist_Invalid_State_And_ErrorJson()
    {
        // Arrange
        var draftSchemaName = IntegrationEndpointHarness.UniqueName("draft_no_id");
        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        var schemasContainer = _harness.BlobClient.GetBlobContainerClient(_harness.SchemasContainerName);
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

        // Act
        var result = (await _harness.Get<IngestDraftSchemaEvent>().HandleAsync(stream, schemaPath, CancellationToken.None)).ShouldSucceed();
        var adminDetailRes = await _harness.Get<GetAdminSchemaVersionDetailEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), draftSchemaName, 1, CancellationToken.None);
        var publicRes = await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), draftSchemaName, CancellationToken.None);

        // Assert
        result.Promoted.Should().BeFalse();
        result.Status.Should().Be("Invalid");
        result.State!.Name.Should().Be("Invalid");
        result.HasErrorReport.Should().BeTrue();

        (await draftContainer.GetBlobClient(schemaPath).ExistsAsync()).Value.Should().BeFalse();
        (await schemasContainer.GetBlobClient($"{draftSchemaName}/v1.error.json").ExistsAsync()).Value.Should().BeTrue();
        (await _harness.Get<ISchemaRegistry>().ExistsAsync(draftSchemaName, 1)).ShouldSucceed().Should().BeTrue();

        var adminDetail = adminDetailRes.ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);
        adminDetail.State.Name.Should().Be("Invalid");
        adminDetail.HasErrorReport.Should().BeTrue();
        adminDetail.ErrorReport.Should().NotBeNull();

        publicRes.ShouldHaveStatusCode(StatusCodes.Status404NotFound);
    }
}
