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
    private static int _urnSequence;
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
    public async Task Survey_Draft_With_Draft_Sibling_Should_Pass_Update_And_Block_Finalise_Under_BlockPolicy()
    {
        // Arrange
        var urn = 880100 + Interlocked.Increment(ref _urnSequence);
        var (baselineSchema, surveySchema) = await SetupBaselineAndSurveySchemasAsync();
        var baselineSubId = await _harness.CreateDraftSubmissionAsync(baselineSchema, 1);
        var surveySubId = await _harness.CreateDraftSubmissionAsync(surveySchema, 1);
        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw($$$"""{"urn":{{{urn}}},"sites":[{"id":"SITE-NORTH"}]}"""),
                baselineSubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        // Act
        var surveyDraft = (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw($$$"""{"urn":{{{urn}}},"siteId":"SITE-NORTH","productType":"Heating"}"""),
                surveySubId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        var prematureFinalise = await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), surveySubId, CancellationToken.None);

        var indexEntriesAfterDraft = (await _harness.Get<IIdentifierIndexRepository>().GetEntriesAsync("URN", urn.ToString())).ShouldSucceed();

        // Assert
        indexEntriesAfterDraft.Should().ContainSingle(e => e.SubmissionId == baselineSubId && e.Status == "Draft");
        surveyDraft.IsSuccessful.Should().BeTrue();
        surveyDraft.PendingErrors.Should().BeEmpty();
        prematureFinalise.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task Survey_Finalise_After_Sibling_Finalised_Should_Resolve_Processed_Sibling_And_Succeed()
    {
        // Arrange
        var urn = 880100 + Interlocked.Increment(ref _urnSequence);
        var (baselineSchema, surveySchema) = await SetupBaselineAndSurveySchemasAsync();
        var baselineSubId = await _harness.CreateDraftSubmissionAsync(baselineSchema, 1);
        var surveySubId = await _harness.CreateDraftSubmissionAsync(surveySchema, 1);
        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();
        var finaliseEndpoint = _harness.Get<PostFinaliseSubmissionEndpoint>();

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw($$$"""{"urn":{{{urn}}},"sites":[{"id":"SITE-NORTH"}]}"""),
                baselineSubId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw($$$"""{"urn":{{{urn}}},"siteId":"SITE-NORTH","productType":"Heating"}"""),
                surveySubId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Act
        var baselineFinalise = (await finaliseEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(), baselineSubId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        var indexEntriesAfterFinalise = (await _harness.Get<IIdentifierIndexRepository>().GetEntriesAsync("URN", urn.ToString())).ShouldSucceed();

        var surveyFinalise = (await finaliseEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(), surveySubId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        // Assert
        baselineFinalise.Identifier.Should().Be(urn.ToString());
        indexEntriesAfterFinalise.Should().ContainSingle(e =>
            e.SubmissionId == baselineSubId &&
            e.Status == "Finalised" &&
            !string.IsNullOrWhiteSpace(e.ProcessedBundlePrefix));

        surveyFinalise.Identifier.Should().Be(urn.ToString());
        surveyFinalise.ProcessedBlobUri.Should().Contain($"urn%3D{urn}/schema%3D{surveySchema}/v%3D1/{surveySubId}/payload.json");
    }

    private async Task<(string BaselineSchema, string SurveySchema)> SetupBaselineAndSurveySchemasAsync()
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
            .AddCelRule("CROSS-SITE-REF", "/", "Site must exist in sibling site submission", "this.siteId in ambient.cross_submission.known_site_ids")
            .AddCelRule("REF-PRODUCT-TYPE", "/", "ProductType must exist in reference data", "this.productType in ambient.reference.product_types")
            .BuildJson();

        await _harness.SubmitAndActivateSchemaAsync(
            baselineSchema, 1, baselineSchemaJson,
            new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true });

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

        return (baselineSchema, surveySchema);
    }

    private static async Task UploadRefBlobAsync(Azure.Storage.Blobs.BlobContainerClient container, string blobName, string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await container.GetBlobClient(blobName).UploadAsync(stream, overwrite: true);
    }
}
