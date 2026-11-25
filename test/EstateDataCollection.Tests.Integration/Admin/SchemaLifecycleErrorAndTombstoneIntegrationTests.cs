using System.Text;
using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.DeleteSchemaVersion;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Schema.SignOffSchema;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.Admin.Events;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class SchemaLifecycleErrorAndTombstoneIntegrationTests(AzuriteFixture fixture)
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

    [Fact]
    public async Task VerifySchema_Always_Returns_200_With_MultiSeverity_Issues_And_AnalyzerFindings()
    {
        const string rulesJson = """
        [
          {
            "id": "R-BAD-CASING",
            "description": "Checks Quantity casing",
            "engine": "cel",
            "path": "/",
            "severity": "ERROR",
            "implementation": "this.Quantity >= 0"
          },
          {
            "id": "R-CRIT-STRICT",
            "description": "Critical quantity floor",
            "engine": "cel",
            "path": "/",
            "severity": "CRITICAL",
            "implementation": "this.quantity >= 50"
          },
          {
            "id": "R-WARN-LOOSE",
            "description": "Warning quantity floor",
            "engine": "cel",
            "path": "/",
            "severity": "WARNING",
            "implementation": "this.quantity >= 20"
          },
          {
            "id": "R-INFO-NOTE",
            "description": "Advisory info note",
            "engine": "cel",
            "path": "/",
            "severity": "INFO",
            "message": "Advisory note"
          }
        ]
        """;

        var dto = new VerifySchemaBodyDto(
            SchemaJson: SchemaWithNumericFieldJson,
            RulesJson: rulesJson,
            SampleValidFixtures: [ValidFixtureJson],
            SampleInvalidFixtures: []);

        var report = (await _harness.Get<PostVerifySchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(dto),
                CancellationToken.None))
            .ShouldBeApiSuccess<VerifySchemaResult>(StatusCodes.Status200OK);

        report.IsSuccessful.Should().BeFalse();
        report.Issues.Should().ContainKey(RuleSeverity.Error);
        report.Issues.Should().ContainKey(RuleSeverity.Warning);
        report.AnalyzerFindings.Should().Contain(f => f.RuleId == "R-BAD-CASING" && f.Severity == RuleSeverity.Error);
        report.AnalyzerFindings.Should().Contain(f => f.RuleId == "R-WARN-LOOSE" && f.Severity == RuleSeverity.Warning);
    }

    [Fact]
    public async Task SubmitSchema_With_Hard_Errors_Persists_Invalid_And_ErrorJson_And_Blocks_Scheduling()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("invalid_sub");
        const string badRulesJson = """
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

        var dto = new SubmitSchemaBodyDto(
            SchemaJson: SchemaWithNumericFieldJson,
            RulesJson: badRulesJson,
            SampleValidFixtures: [ValidFixtureJson]);

        var submitted = (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(dto),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);

        submitted.State!.Name.Should().Be("Invalid");
        submitted.HasErrorReport.Should().BeTrue();
        submitted.Issues.Should().ContainKey(RuleSeverity.Error);

        // .error.json exists in schemas container & surfaced on Admin detail
        var schemasContainer = _harness.BlobClient.GetBlobContainerClient(_harness.SchemasContainerName);
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.error.json").ExistsAsync()).Value.Should().BeTrue();

        var detail = (await _harness.Get<GetAdminSchemaVersionDetailEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);
        detail.State.Name.Should().Be("Invalid");
        detail.HasErrorReport.Should().BeTrue();
        detail.ErrorReport.Should().NotBeNull();

        // Admin catalogue lists Invalid version; Public catalogue hides it
        var adminList = (await _harness.Get<GetAdminSchemasEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSchemasResult>(StatusCodes.Status200OK);
        adminList.Schemas.Should().Contain(c => c.SchemaName == schemaName && c.Versions.Any(v => v.State.Name == "Invalid" && v.HasErrorReport));

        (await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                schemaName,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status404NotFound);

        // Scheduling an Invalid schema fails with Schema.Invalid (422)
        var scheduleDto = new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30));
        var scheduleResp = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(scheduleDto),
            schemaName,
            1,
            CancellationToken.None);
        scheduleResp.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task WarningsOnly_Schema_Persists_Unvalidated_Blocks_Schedule_Until_SignOff_Then_Activates()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("warn_signoff");
        const string warningRulesJson = """
        [
          {
            "id": "R-CRIT-STRICT",
            "description": "Critical quantity floor",
            "engine": "cel",
            "path": "/",
            "severity": "CRITICAL",
            "implementation": "this.quantity >= 50"
          },
          {
            "id": "R-WARN-LOOSE",
            "description": "Warning quantity floor",
            "engine": "cel",
            "path": "/",
            "severity": "WARNING",
            "implementation": "this.quantity >= 20"
          }
        ]
        """;

        var submitDto = new SubmitSchemaBodyDto(
            SchemaJson: SchemaWithNumericFieldJson,
            RulesJson: warningRulesJson,
            SampleValidFixtures: [ValidFixtureJson]);

        var submitted = (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(submitDto),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);

        submitted.State!.Name.Should().Be("Unvalidated");
        submitted.HasErrorReport.Should().BeTrue();
        submitted.Issues.Should().ContainKey(RuleSeverity.Warning);

        // Blocked from scheduling before sign-off
        var scheduleDto = new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30));
        (await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(scheduleDto),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        // Sign off via PATCH /api/schemas/{name}/versions/{v}/signoff
        var signOffResult = (await _harness.Get<PatchSignOffSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SignOffSchemaBodyDto("estates-lead@education.gov.uk")),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<SignOffSchemaResult>(StatusCodes.Status200OK);

        signOffResult.State.Name.Should().Be("Validated");
        signOffResult.SignedOffBy.Should().Be("estates-lead@education.gov.uk");

        // Now scheduling succeeds and Public API exposes Active schema
        (await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(scheduleDto),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        var pub = (await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                schemaName,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);
        pub.ActiveVersion.Should().Be(1);
    }

    [Fact]
    public async Task DeleteSchemaVersion_Creates_Tombstone_Keeping_Meta_And_ErrorJson_And_Forces_VPlus1()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("del_tombstone");
        const string brokenRulesJson = """
        [
          {
            "id": "R-MISSING",
            "description": "Missing ghost field",
            "engine": "cel",
            "path": "/",
            "severity": "ERROR",
            "implementation": "this.ghostField > 0"
          }
        ]
        """;

        // 1. Submit v1 with error -> Invalid + .error.json
        await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                SchemaJson: SchemaWithNumericFieldJson,
                RulesJson: brokenRulesJson,
                SampleValidFixtures: [ValidFixtureJson])),
            schemaName,
            1,
            CancellationToken.None);

        // 2. Delete v1 -> removes payload blobs, marks .meta.json = Deleted, keeps .meta.json & .error.json
        var deleted = (await _harness.Get<DeleteSchemaVersionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<DeleteSchemaVersionResult>(StatusCodes.Status200OK);

        deleted.State.Name.Should().Be("Deleted");
        deleted.ErrorReportPreserved.Should().BeTrue();

        var schemasContainer = _harness.BlobClient.GetBlobContainerClient(_harness.SchemasContainerName);
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.schema.json").ExistsAsync()).Value.Should().BeFalse();
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.rules.json").ExistsAsync()).Value.Should().BeFalse();
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.valid.json").ExistsAsync()).Value.Should().BeFalse();
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.meta.json").ExistsAsync()).Value.Should().BeTrue();
        (await schemasContainer.GetBlobClient($"{schemaName}/v1.error.json").ExistsAsync()).Value.Should().BeTrue();

        // 3. Reusing consumed v1 via HTTP returns 409 Conflict
        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: SchemaWithNumericFieldJson,
                    SampleValidFixtures: [ValidFixtureJson])),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status409Conflict);

        // 4. Reusing consumed v1 via Event Grid draft drop also returns Conflict
        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        await draftContainer.CreateIfNotExistsAsync();
        var draftV1Path = $"{schemaName}/v1.schema.json";
        await draftContainer.GetBlobClient(draftV1Path).UploadAsync(BinaryData.FromString(SchemaWithNumericFieldJson), overwrite: true);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SchemaWithNumericFieldJson));
        var draftV1Result = await _harness.Get<IngestDraftSchemaEvent>().HandleAsync(stream, draftV1Path, CancellationToken.None);
        draftV1Result.IsFailure.Should().BeTrue();
        draftV1Result.Error.Code.Should().Be("Schema.AlreadyExists");

        // 5. Fixing and pushing v2 succeeds cleanly
        var v2 = (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: SchemaWithNumericFieldJson,
                    SampleValidFixtures: [ValidFixtureJson])),
                schemaName,
                2,
                CancellationToken.None))
            .ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);
        v2.Version.Should().Be(2);
        v2.State!.Name.Should().Be("Validated");
    }
}
