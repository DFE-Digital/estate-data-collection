using System.Text;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.EndToEnd;

[Collection("AzuriteCollection")]
public sealed class CrossSubmissionAndReferenceDataIntegrationTests(AzuriteFixture fixture)
    : IAsyncLifetime
{
    private readonly string _referenceDataContainerName = $"refdata{Guid.NewGuid():N}"[..24];
    private IntegrationEndpointHarness _harness = null!;

    public async Task InitializeAsync()
    {
        var refContainer = fixture.Client.GetBlobContainerClient(_referenceDataContainerName);
        await refContainer.CreateIfNotExistsAsync();

        await UploadRefBlobAsync(refContainer, "product-types.json", """["Heating","Roofing"]""");

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
    public async Task CrossSubmission_Index_And_ReferenceData_Should_Enforce_DraftPolicy_And_Resolve_After_Sibling_Finalise()
    {
        var baselineSchema = IntegrationEndpointHarness.UniqueName("site_base");
        var surveySchema = IntegrationEndpointHarness.UniqueName("elem_surv");

        const string baselineSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["urn", "sites"],
          "properties": {
            "urn": { "type": "integer", "minimum": 100000 },
            "sites": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["id"],
                "properties": { "id": { "type": "string" } }
              }
            }
          }
        }
        """;

        const string surveySchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["urn", "siteId", "productType"],
          "properties": {
            "urn": { "type": "integer", "minimum": 100000 },
            "siteId": { "type": "string" },
            "productType": { "type": "string" }
          }
        }
        """;

        var surveyRules = new QualityRulesBuilder()
            .AddCelRule(
                "CROSS-SITE-REF",
                "/",
                "Site must exist in sibling site submission",
                "this.siteId in ambient.cross_submission.known_site_ids")
            .AddCelRule(
                "REF-PRODUCT-TYPE",
                "/",
                "ProductType must exist in reference data",
                "this.productType in ambient.reference.product_types")
            .BuildJson();

        // Register & activate baseline schema
        await _harness.SubmitAndActivateSchemaAsync(
            baselineSchema,
            1,
            baselineSchemaJson,
            new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true });

        // Register & activate survey schema with BlockFinaliseOnDraftReferences policy
        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: surveySchemaJson,
                    RulesJson: surveyRules,
                    IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true },
                    ValidationPolicy: new SchemaValidationPolicy
                    {
                        FailOnMinimumSeverity = RuleSeverity.Error,
                        CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.BlockFinaliseOnDraftReferences
                    })),
                surveySchema, 1, CancellationToken.None))
            .ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);

        (await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                    EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                    EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30),
                    LifecycleAction: "Activate")),
                surveySchema, 1, CancellationToken.None))
            .ShouldBeApiSuccess<ScheduleSchemaResult>(StatusCodes.Status200OK);

        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();
        var finaliseEndpoint = _harness.Get<PostFinaliseSubmissionEndpoint>();
        var indexRepo = _harness.Get<IIdentifierIndexRepository>();

        // 1. Create baseline submission and upload Draft (not yet finalised)
        var baselineSubId = await _harness.CreateDraftSubmissionAsync(baselineSchema, 1);
        var baselineDraft = (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":880101,"sites":[{"id":"SITE-NORTH"}]}"""),
                baselineSubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        baselineDraft.IsSuccessful.Should().BeTrue();

        // Index should contain Draft entry for URN 880101
        var indexEntriesAfterDraft = (await indexRepo.GetEntriesAsync("URN", "880101")).ShouldSucceed();
        indexEntriesAfterDraft.Should().ContainSingle(e => e.SubmissionId == baselineSubId && e.Status == "Draft");

        // 2. Create survey submission referencing SITE-NORTH and Heating -> PUT Draft succeeds (cross-submission + ref data resolved)
        var surveySubId = await _harness.CreateDraftSubmissionAsync(surveySchema, 1);
        var surveyDraft = (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":880101,"siteId":"SITE-NORTH","productType":"Heating"}"""),
                surveySubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        surveyDraft.IsSuccessful.Should().BeTrue();
        surveyDraft.PendingErrors.Should().BeEmpty();

        // 3. Finalise survey while baseline is still Draft -> blocked (422) by CrossSubmissionDraftPolicy.BlockFinaliseOnDraftReferences
        var prematureFinalise = await finaliseEndpoint.HandleAsync(HttpRequestBuilder.Empty(), surveySubId, CancellationToken.None);
        prematureFinalise.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        // 4. Finalise baseline submission -> transitions index entry to Finalised with ProcessedBundlePrefix
        var baselineFinalise = (await finaliseEndpoint.HandleAsync(HttpRequestBuilder.Empty(), baselineSubId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);
        baselineFinalise.Identifier.Should().Be("880101");

        var indexEntriesAfterFinalise = (await indexRepo.GetEntriesAsync("URN", "880101")).ShouldSucceed();
        indexEntriesAfterFinalise.Should().ContainSingle(e =>
            e.SubmissionId == baselineSubId &&
            e.Status == "Finalised" &&
            !string.IsNullOrWhiteSpace(e.ProcessedBundlePrefix));

        // 5. Finalise survey submission now succeeds (200 OK) reading finalised sibling from processed bundle!
        var surveyFinalise = (await finaliseEndpoint.HandleAsync(HttpRequestBuilder.Empty(), surveySubId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);
        surveyFinalise.Identifier.Should().Be("880101");
        surveyFinalise.ProcessedBlobUri.Should().Contain($"urn%3D880101/schema%3D{surveySchema}/v%3D1/{surveySubId}/payload.json");
    }

    private static async Task UploadRefBlobAsync(Azure.Storage.Blobs.BlobContainerClient container, string blobName, string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await container.GetBlobClient(blobName).UploadAsync(stream, overwrite: true);
    }
}
