using System.Text;
using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.DeleteSchemaVersion;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.AzureFunctions.Admin.Events;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class InvalidSchemaAndTombstoneLifecycleTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    private const string SchemaWithNumericFieldJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["urn", "quantity"],
      "properties": {
        "urn": { "type": "integer", "minimum": 1 },
        "quantity": { "type": "number", "minimum": 0, "maximum": 100 }
      }
    }
    """;

    private const string ValidFixtureJson = """{"urn": 100001, "quantity": 60}""";

    private const string BrokenRulesJson = """
    [
      {
        "id": "R-MISSING-FIELD",
        "description": "Missing field test",
        "engine": "cel",
        "path": "/",
        "severity": "ERROR",
        "implementation": "this.nonExistentField > 0"
      }
    ]
    """;

    [Fact]
    public async Task SubmitSchema_With_Hard_Errors_Should_Persist_Invalid_State_And_ErrorJson()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("invalid_sub");
        var dto = new SubmitSchemaBodyDto(
            SchemaJson: SchemaWithNumericFieldJson,
            RulesJson: BrokenRulesJson,
            SampleValidFixtures: [ValidFixtureJson]);

        // Act
        var submitted = (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(dto),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);

        var schemasContainer = _harness.BlobClient.GetBlobContainerClient(_harness.SchemasContainerName);
        var errorJsonExists = (await schemasContainer.GetBlobClient($"{schemaName}/v1.error.json").ExistsAsync()).Value;

        var detail = (await _harness.Get<GetAdminSchemaVersionDetailEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);

        // Assert
        submitted.State!.Name.Should().Be("Invalid");
        submitted.HasErrorReport.Should().BeTrue();
        submitted.Issues.Should().ContainKey(RuleSeverity.Error);
        errorJsonExists.Should().BeTrue();
        detail.State.Name.Should().Be("Invalid");
        detail.HasErrorReport.Should().BeTrue();
        detail.ErrorReport.Should().NotBeNull();
    }

    [Fact]
    public async Task Invalid_Schema_Should_Be_Listed_On_Admin_Hidden_From_Public_And_Blocked_From_Scheduling()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("invalid_vis");
        await SubmitInvalidV1Async(schemaName);

        var scheduleDto = new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30));

        // Act
        var adminList = (await _harness.Get<GetAdminSchemasEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSchemasResult>(StatusCodes.Status200OK);

        var publicRes = await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(),
            schemaName,
            CancellationToken.None);

        var scheduleResp = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(scheduleDto),
            schemaName,
            1,
            CancellationToken.None);

        // Assert
        adminList.Schemas.Should().Contain(c => c.SchemaName == schemaName && c.Versions.Any(v => v.State.Name == "Invalid" && v.HasErrorReport));
        publicRes.ShouldHaveStatusCode(StatusCodes.Status404NotFound);
        scheduleResp.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task DeleteSchemaVersion_Should_Create_Tombstone_Preserving_Meta_And_ErrorJson()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("del_tomb");
        await SubmitInvalidV1Async(schemaName);

        // Act
        var deleted = (await _harness.Get<DeleteSchemaVersionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<DeleteSchemaVersionResult>(StatusCodes.Status200OK);

        var schemasContainer = _harness.BlobClient.GetBlobContainerClient(_harness.SchemasContainerName);

        // Assert
        deleted.State.Name.Should().Be("Deleted");
        deleted.ErrorReportPreserved.Should().BeTrue();
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.schema.json").ExistsAsync()).Value.Should().BeFalse();
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.rules.json").ExistsAsync()).Value.Should().BeFalse();
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.valid.json").ExistsAsync()).Value.Should().BeFalse();
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.meta.json").ExistsAsync()).Value.Should().BeTrue();
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.error.json").ExistsAsync()).Value.Should().BeTrue();
    }

    [Fact]
    public async Task Deleted_Version_Should_Reject_Reupload_And_Allow_Fixed_Next_Version()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("del_v2");
        await SubmitInvalidV1Async(schemaName);
        (await _harness.Get<DeleteSchemaVersionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        await draftContainer.CreateIfNotExistsAsync();
        var draftV1Path = $"{schemaName}/v1.schema.json";
        await draftContainer.GetBlobClient(draftV1Path).UploadAsync(BinaryData.FromString(SchemaWithNumericFieldJson), overwrite: true);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SchemaWithNumericFieldJson));

        // Act
        var httpReuploadRes = await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                SchemaJson: SchemaWithNumericFieldJson,
                SampleValidFixtures: [ValidFixtureJson])),
            schemaName,
            1,
            CancellationToken.None);

        var draftV1Result = await _harness.Get<IngestDraftSchemaEvent>().HandleAsync(stream, draftV1Path, CancellationToken.None);

        var v2 = (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: SchemaWithNumericFieldJson,
                    SampleValidFixtures: [ValidFixtureJson])),
                schemaName,
                2,
                CancellationToken.None))
            .ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);

        // Assert
        httpReuploadRes.ShouldHaveStatusCode(StatusCodes.Status409Conflict);
        draftV1Result.IsFailure.Should().BeTrue();
        draftV1Result.Error.Code.Should().Be("Schema.AlreadyExists");
        v2.Version.Should().Be(2);
        v2.State!.Name.Should().Be("Validated");
    }

    private async Task SubmitInvalidV1Async(string schemaName)
    {
        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: SchemaWithNumericFieldJson,
                    RulesJson: BrokenRulesJson,
                    SampleValidFixtures: [ValidFixtureJson])),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);
    }
}
