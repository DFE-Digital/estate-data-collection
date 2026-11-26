using System.Net;
using AwesomeAssertions;
using EstateDataCollection.AzureFunctions.Admin.Routes.Admin;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EstateDataCollection.Tests.Integration.AdminUi;

[Collection("AzuriteCollection")]
public sealed class AdminUiStatisticsAndSubmissionInspectorIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    private const string MultiChunkSchemaJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["urn", "site", "blocks"],
      "properties": {
        "urn": { "type": "integer", "minimum": 100000 },
        "site": {
          "type": "object",
          "required": ["name", "postcode"],
          "properties": {
            "name": { "type": "string", "minLength": 1 },
            "postcode": { "type": "string", "minLength": 1 }
          }
        },
        "blocks": {
          "type": "array",
          "minItems": 1,
          "items": { "type": "string" }
        }
      }
    }
    """;

    [Fact]
    public async Task StatisticsPage_WhenFilteredBySchema_Should_Render_Matching_Draft_And_Finalised_Submissions()
    {
        // Arrange
        var seeded = await SeedStatisticsDatasetAsync();

        // Act
        var statsRes = await _harness.Get<GetStatisticsPage>().HandleAsync(
            new HttpRequestBuilder().WithQuery("schemaName", seeded.SchemaA).Build(),
            CancellationToken.None);

        // Assert
        var statsHtml = WebUtility.HtmlDecode(statsRes.Should().BeOfType<ContentResult>().Subject.Content!);
        statsHtml.Should().Contain("Submission Statistics");
        statsHtml.Should().Contain($"{seeded.SchemaA} v1");
        statsHtml.Should().Contain($"/manage/submissions?schemaName={Uri.EscapeDataString(seeded.SchemaA)}&version=1&status=Draft");
        statsHtml.Should().Contain($"/manage/submissions/{seeded.FinalisedId}");
        statsHtml.Should().Contain($"/manage/submissions/{seeded.InProgressId}");
        statsHtml.Should().NotContain(seeded.EmptyDraftOnOtherSchemaId.ToString());
    }

    [Fact]
    public async Task SubmissionsListPage_WhenFilteredByDraftStatus_Should_Exclude_Finalised_Submissions()
    {
        // Arrange
        var seeded = await SeedStatisticsDatasetAsync();

        // Act
        var draftFilterRes = await _harness.Get<GetSubmissionsPage>().HandleAsync(
            new HttpRequestBuilder()
                .WithQuery("schemaName", seeded.SchemaA)
                .WithQuery("status", "Draft")
                .Build(),
            CancellationToken.None);

        // Assert
        var draftFilterHtml = WebUtility.HtmlDecode(draftFilterRes.Should().BeOfType<ContentResult>().Subject.Content!);
        draftFilterHtml.Should().Contain(seeded.InProgressId.ToString());
        draftFilterHtml.Should().NotContain(seeded.FinalisedId.ToString());
    }

    [Fact]
    public async Task SubmissionDetailPage_For_DraftSubmission_Should_Render_PendingIssues_And_DraftPreview()
    {
        // Arrange
        var seeded = await SeedStatisticsDatasetAsync();

        // Act
        var draftDetailRes = await _harness.Get<GetSubmissionDetailPage>().HandleAsync(
            HttpRequestBuilder.Empty(),
            seeded.InProgressId,
            CancellationToken.None);

        // Assert
        var draftDetailHtml = WebUtility.HtmlDecode(draftDetailRes.Should().BeOfType<ContentResult>().Subject.Content!);
        draftDetailHtml.Should().Contain(seeded.InProgressId.ToString());
        draftDetailHtml.Should().Contain("Validation Status & Next Steps");
        draftDetailHtml.Should().Contain("Pending Validation Issues");
        draftDetailHtml.Should().Contain("Error (");
        draftDetailHtml.Should().Contain("Current Merged Draft Preview");
        draftDetailHtml.Should().Contain("Draft Upload #1");
        draftDetailHtml.Should().Contain("Draft School");
    }

    [Fact]
    public async Task SubmissionDetailPage_For_FinalisedSubmission_Should_Render_Bundle_Layout_Chunks_And_Audit()
    {
        // Arrange
        var seeded = await SeedStatisticsDatasetAsync();

        // Act
        var finalisedDetailRes = await _harness.Get<GetSubmissionDetailPage>().HandleAsync(
            HttpRequestBuilder.Empty(),
            seeded.FinalisedId,
            CancellationToken.None);

        // Assert
        var finalisedDetailHtml = WebUtility.HtmlDecode(finalisedDetailRes.Should().BeOfType<ContentResult>().Subject.Content!);
        finalisedDetailHtml.Should().Contain(seeded.FinalisedId.ToString());
        finalisedDetailHtml.Should().Contain("Processed Canonical Envelope");
        finalisedDetailHtml.Should().Contain("Bundle folder:");
        finalisedDetailHtml.Should().Contain($"urn=104521/schema={seeded.SchemaA}/v=1/{seeded.FinalisedId}/");
        finalisedDetailHtml.Should().Contain("SW1P 3BT");
        finalisedDetailHtml.Should().Contain("Draft Upload #1");
        finalisedDetailHtml.Should().Contain("Draft Upload #2");
        finalisedDetailHtml.Should().Contain("Audit Trail & Processes Consumed");
        finalisedDetailHtml.Should().Contain("DraftPayloadMerger");
        finalisedDetailHtml.Should().Contain("BlobStorage:PromoteToProcessed");
    }

    private async Task<(string SchemaA, Guid EmptyDraftOnOtherSchemaId, Guid InProgressId, Guid FinalisedId)> SeedStatisticsDatasetAsync()
    {
        var schemaA = IntegrationEndpointHarness.UniqueName("stats_a");
        var schemaB = IntegrationEndpointHarness.UniqueName("stats_b");

        await _harness.SubmitAndActivateSchemaAsync(schemaA, 1, MultiChunkSchemaJson);
        await _harness.SubmitAndActivateSchemaAsync(schemaB, 1, JsonSchemaFixtures.ValidV1Schema);

        var emptyDraftId = await _harness.CreateDraftSubmissionAsync(schemaB, 1);
        var inProgressId = await _harness.CreateDraftSubmissionAsync(schemaA, 1);
        var finalisedId = await _harness.CreateDraftSubmissionAsync(schemaA, 1);

        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();
        var finaliseEndpoint = _harness.Get<PostFinaliseSubmissionEndpoint>();

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":109999,"site":{"name":"Draft School"}}"""),
                inProgressId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":104521,"site":{"name":"Oak Primary"},"blocks":["Block A"]}"""),
                finalisedId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"site":{"postcode":"SW1P 3BT"},"blocks":["Block A","Block B"]}"""),
                finalisedId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        (await finaliseEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                finalisedId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        return (schemaA, emptyDraftId, inProgressId, finalisedId);
    }
}
