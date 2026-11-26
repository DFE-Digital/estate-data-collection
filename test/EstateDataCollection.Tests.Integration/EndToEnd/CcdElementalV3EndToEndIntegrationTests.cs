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
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Builders.Payloads;
using EstateDataCollection.Tests.Common.Extensions;
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

        var referenceBuilder = new CcdElementalV3ReferenceDataBuilder();
        await UploadJsonBlobAsync(refContainer, "product-types.json", referenceBuilder.BuildProductTypesJson());
        await UploadJsonBlobAsync(refContainer, "repair-items-by-product-type.json", referenceBuilder.BuildRepairItemsByProductTypeJson());
        await UploadJsonBlobAsync(refContainer, "nrm3-codes.json", referenceBuilder.BuildNrm3CodesJson());

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
    public async Task CcdElementalV3_Draft_Update_Should_Enrich_ReferenceData_And_Surface_Info_Issues()
    {
        // Arrange
        await SeedBaselineSiteAndBlockSubmissionAsync();
        var ccdSchemaName = await SubmitAndActivateCcdContractAsync();
        var ccdSubId = await _harness.CreateDraftSubmissionAsync(ccdSchemaName, 3);

        // Act
        var draftRes = (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(CcdElementalV3PayloadBuilder.DefaultValidPayloadJson),
                ccdSubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        // Assert
        draftRes.IsSuccessful.Should().BeTrue();
        draftRes.PendingErrors.Should().BeEmpty();
        draftRes.Issues.Should().NotBeNull();
        draftRes.Issues![RuleSeverity.Info].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-038");
    }

    [Fact]
    public async Task CcdElementalV3_Finalise_Should_Promote_Bundle_With_Payload_Session_And_FiveStep_Audit()
    {
        // Arrange
        await SeedBaselineSiteAndBlockSubmissionAsync();
        var ccdSchemaName = await SubmitAndActivateCcdContractAsync();
        var ccdSubId = await _harness.CreateDraftSubmissionAsync(ccdSchemaName, 3);

        (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(CcdElementalV3PayloadBuilder.DefaultValidPayloadJson),
                ccdSubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        // Act
        var finaliseRes = (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                ccdSubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        var bundlePrefix = $"urn=104521/schema={ccdSchemaName}/v=3/{ccdSubId}/";
        var processedContainer = _harness.BlobClient.GetBlobContainerClient(_harness.ProcessedContainerName);
        var bundleBlobs = new List<string>();
        await foreach (var blob in processedContainer.GetBlobsAsync(
                           BlobTraits.None,
                           BlobStates.None,
                           prefix: bundlePrefix,
                           cancellationToken: CancellationToken.None))
        {
            bundleBlobs.Add(blob.Name);
        }

        var payloadBlobText = (await processedContainer.GetBlobClient($"{bundlePrefix}payload.json").DownloadContentAsync())
            .Value.Content.ToString();
        var detail = (await _harness.Get<IUseCase<GetSubmissionDetailResult, GetSubmissionDetailRequest>>().ExecuteAsync(
                new GetSubmissionDetailRequest(ccdSubId),
                CancellationToken.None))
            .ShouldSucceed();

        // Assert
        finaliseRes.Identifier.Should().Be("104521");
        finaliseRes.IdentifierType.Should().Be("URN");
        finaliseRes.Issues.Should().NotBeNull();
        finaliseRes.Issues![RuleSeverity.Info].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-038");

        bundleBlobs.Should().Contain($"{bundlePrefix}payload.json");
        bundleBlobs.Should().Contain($"{bundlePrefix}session.json");
        bundleBlobs.Should().Contain(b => b.StartsWith($"{bundlePrefix}audit/", StringComparison.Ordinal) && b.EndsWith(".json", StringComparison.Ordinal));

        using var doc = JsonDocument.Parse(payloadBlobText);
        doc.RootElement.GetProperty("school").GetProperty("urn").GetString().Should().Be("104521");
        doc.RootElement.GetProperty("elements").GetArrayLength().Should().Be(3);

        detail.ProcessedArtifact.Should().NotBeNull();
        detail.AuditEntries.Should().NotBeEmpty();
        detail.AuditEntries.Last().ProcessesConsumed.Select(p => p.ProcessName).Should().ContainInOrder(
            SubmissionProcessCatalog.JsonSchemaValidator.ProcessName,
            SubmissionProcessCatalog.IdentifierExtractor.ProcessName,
            SubmissionProcessCatalog.CrossSubmissionEnrichment.ProcessName,
            SubmissionProcessCatalog.ReferenceDataEnrichment.ProcessName,
            SubmissionProcessCatalog.QualityRulesValidator.ProcessName);
    }

    private async Task SeedBaselineSiteAndBlockSubmissionAsync()
    {
        var siteSchemaName = IntegrationEndpointHarness.UniqueName("edc_survey_sites");
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

        await _harness.SubmitAndActivateSchemaAsync(
            siteSchemaName,
            1,
            siteSchemaJson,
            new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true });

        var baselineSubId = await _harness.CreateDraftSubmissionAsync(siteSchemaName, 1);

        (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
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

        (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                baselineSubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);
    }

    private async Task<string> SubmitAndActivateCcdContractAsync()
    {
        var ccdSchemaName = IntegrationEndpointHarness.UniqueName("ccd_elemental_v3");
        var submitCcd = (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    OdcsYaml: CcdElementalV3ContractBuilder.DefaultOdcsYaml,
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

        return ccdSchemaName;
    }

    private static async Task UploadJsonBlobAsync(BlobContainerClient container, string targetBlobName, string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await container.GetBlobClient(targetBlobName).UploadAsync(stream, overwrite: true);
    }
}
