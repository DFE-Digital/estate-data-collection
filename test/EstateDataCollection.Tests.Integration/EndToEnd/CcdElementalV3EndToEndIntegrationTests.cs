using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionDetail;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.EndToEnd;

[Collection("AzuriteCollection")]
public sealed class CcdElementalV3EndToEndIntegrationTests(AzuriteFixture fixture)
    : IAsyncLifetime
{
    private readonly string _referenceDataContainerName = $"ccdref{Guid.NewGuid():N}"[..24];
    private IntegrationEndpointHarness _harness = null!;

    public async Task InitializeAsync()
    {
        var refContainer = fixture.Client.GetBlobContainerClient(_referenceDataContainerName);
        await refContainer.CreateIfNotExistsAsync();

        await UploadReferenceFixtureAsync(refContainer, "reference-data/ccd-elemental-v3/product-types.json", "product-types.json");
        await UploadReferenceFixtureAsync(refContainer, "reference-data/ccd-elemental-v3/repair-items-by-product-type.json", "repair-items-by-product-type.json");
        await UploadReferenceFixtureAsync(refContainer, "reference-data/ccd-elemental-v3/nrm3-codes.json", "nrm3-codes.json");

        _harness = new IntegrationEndpointHarness(
            fixture,
            configurationOverrides: new Dictionary<string, string?>
            {
                [$"{BlobSubmissionRepositoryOptions.SectionName}:ReferenceDataContainerName"] = _referenceDataContainerName
            });
    }

    public async Task DisposeAsync()
    {
        _harness.Dispose();
        await fixture.Client.GetBlobContainerClient(_referenceDataContainerName).DeleteIfExistsAsync();
    }

    [Fact]
    public async Task CcdElementalV3_OdcsContract_Should_Validate_Against_Azurite_ReferenceData_And_CrossSubmission_Into_ProcessedBundle()
    {
        var siteSchemaName = IntegrationEndpointHarness.UniqueName("edc_survey_sites");
        var ccdSchemaName = IntegrationEndpointHarness.UniqueName("ccd_elemental_v3");

        const string siteSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["urn", "sites"],
          "properties": {
            "urn": { "type": "string", "minLength": 1 },
            "sites": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["id"],
                "properties": {
                  "id": { "type": "string" },
                  "blocks": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "required": ["id"],
                      "properties": { "id": { "type": "string" } }
                    }
                  }
                }
              }
            }
          }
        }
        """;

        // 1. Register & activate sibling baseline site/block schema, then submit & finalise baseline for URN 104521
        await _harness.SubmitAndActivateSchemaAsync(
            siteSchemaName,
            1,
            siteSchemaJson,
            new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true });

        var baselineSubId = await _harness.CreateDraftSubmissionAsync(siteSchemaName, 1);
        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();
        var finaliseEndpoint = _harness.Get<PostFinaliseSubmissionEndpoint>();

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""
                {
                  "urn": "104521",
                  "sites": [
                    {
                      "id": "SITE-MAIN",
                      "blocks": [{ "id": "BLK-A" }, { "id": "BLK-B" }]
                    }
                  ]
                }
                """),
                baselineSubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        (await finaliseEndpoint.HandleAsync(HttpRequestBuilder.Empty(), baselineSubId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        // 2. Submit & activate CCD Elemental v3 ODCS v3.2.0 contract
        var odcsYaml = CdeContractFixtureLoader.LoadFile("contracts/ccd-elemental-v3.odcs.yaml");
        var submitCcd = (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    OdcsYaml: odcsYaml,
                    FileName: "ccd-elemental-v3.odcs.yaml",
                    IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true })),
                ccdSchemaName,
                3,
                CancellationToken.None))
            .ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);
        submitCcd.State!.Name.Should().Be("Validated");

        (await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                    EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                    EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30),
                    LifecycleAction: "Activate")),
                ccdSchemaName,
                3,
                CancellationToken.None))
            .ShouldBeApiSuccess<ScheduleSchemaResult>(StatusCodes.Status200OK);

        // 3. Draft & finalise valid CCD Elemental v3 fixture
        var ccdSubId = await _harness.CreateDraftSubmissionAsync(ccdSchemaName, 3);
        var validPayload = CdeContractFixtureLoader.LoadFile("fixtures/ccd-elemental-v3.valid.json");

        var draftRes = (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw(validPayload),
                ccdSubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        draftRes.IsSuccessful.Should().BeTrue();
        draftRes.PendingErrors.Should().BeEmpty();
        draftRes.Issues.Should().NotBeNull();
        draftRes.Issues![RuleSeverity.Info].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-038");

        var finaliseRes = (await finaliseEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                ccdSubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        finaliseRes.Identifier.Should().Be("104521");
        finaliseRes.IdentifierType.Should().Be("URN");
        finaliseRes.Issues.Should().NotBeNull();
        finaliseRes.Issues![RuleSeverity.Info].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-038");

        // 4. Verify processed bundle layout: urn=104521/schema={ccdSchemaName}/v=3/{ccdSubId}/
        var bundlePrefix = $"urn=104521/schema={ccdSchemaName}/v=3/{ccdSubId}/";
        var processedContainer = _harness.BlobClient.GetBlobContainerClient(_harness.ProcessedContainerName);
        var bundleBlobs = new List<string>();
        await foreach (var blob in processedContainer.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: bundlePrefix, cancellationToken: CancellationToken.None))
        {
            bundleBlobs.Add(blob.Name);
        }

        bundleBlobs.Should().Contain($"{bundlePrefix}payload.json");
        bundleBlobs.Should().Contain($"{bundlePrefix}session.json");
        bundleBlobs.Should().Contain(b => b.StartsWith($"{bundlePrefix}audit/", StringComparison.Ordinal) && b.EndsWith(".json", StringComparison.Ordinal));

        var payloadBlobText = (await processedContainer.GetBlobClient($"{bundlePrefix}payload.json").DownloadContentAsync())
            .Value.Content.ToString();
        using (var doc = JsonDocument.Parse(payloadBlobText))
        {
            doc.RootElement.GetProperty("school").GetProperty("urn").GetString().Should().Be("104521");
            doc.RootElement.GetProperty("elements").GetArrayLength().Should().Be(3);
        }

        // 5. Verify submission audit trail captures the full 5-step workflow pipeline
        var detail = (await _harness.Get<IUseCase<GetSubmissionDetailResult, GetSubmissionDetailRequest>>().ExecuteAsync(
                new GetSubmissionDetailRequest(ccdSubId),
                CancellationToken.None))
            .ShouldSucceed();

        detail.ProcessedArtifact.Should().NotBeNull();
        detail.AuditEntries.Should().NotBeEmpty();
        var finalAudit = detail.AuditEntries.Last();
        finalAudit.ProcessesConsumed.Select(p => p.ProcessName).Should().ContainInOrder(
            SubmissionProcessCatalog.JsonSchemaValidator.ProcessName,
            SubmissionProcessCatalog.IdentifierExtractor.ProcessName,
            SubmissionProcessCatalog.CrossSubmissionEnrichment.ProcessName,
            SubmissionProcessCatalog.ReferenceDataEnrichment.ProcessName,
            SubmissionProcessCatalog.QualityRulesValidator.ProcessName);
    }

    private static async Task UploadReferenceFixtureAsync(
        BlobContainerClient container,
        string fixtureRelativePath,
        string targetBlobName)
    {
        var content = CdeContractFixtureLoader.LoadFile(fixtureRelativePath);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await container.GetBlobClient(targetBlobName).UploadAsync(stream, overwrite: true);
    }
}
