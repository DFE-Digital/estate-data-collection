using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Admin;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Tests.Common.Builders;
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

    [Fact]
    public async Task Timestamped_Draft_Merge_Check_NextSteps_And_Admin_Statistics_Inspector_Should_Work_EndToEnd()
    {
        var schemaA = IntegrationEndpointHarness.UniqueName("stats_a");
        var schemaB = IntegrationEndpointHarness.UniqueName("stats_b");

        const string schemaAJson = """
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

        var rulesJson = new QualityRulesBuilder()
            .AddCelRule("CEL-URN-RANGE", "urn", "URN must be >= 100000", "urn >= 100000")
            .BuildJson();

        // Submit and activate Schema A (with CEL rule) and Schema B
        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(schemaAJson, RulesJson: rulesJson)),
                schemaA, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);

        (await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow.AddDays(30),
                    LifecycleAction: "Activate")),
                schemaA, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        await _harness.SubmitAndActivateSchemaAsync(schemaB, 1, JsonSchemaFixtures.ValidV1Schema);

        // 1. Empty draft on schemaB
        var emptyDraftId = await _harness.CreateDraftSubmissionAsync(schemaB, 1);

        // 2. Multi-chunk timestamped submission on schemaA
        var mergedDraftId = await _harness.CreateDraftSubmissionAsync(schemaA, 1);

        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();
        var checkEndpoint = _harness.Get<GetCheckSubmissionEndpoint>();
        var finaliseEndpoint = _harness.Get<PostFinaliseSubmissionEndpoint>();

        // Chunk 1: URN + site.name + initial blocks array
        var put1 = (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":104521,"site":{"name":"Oak Primary"},"blocks":["Block A"]}"""),
                mergedDraftId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        put1.IsSuccessful.Should().BeFalse();
        put1.DraftCount.Should().Be(1);
        put1.NextSteps.Should().NotBeNullOrEmpty();

        // Check endpoint before completing -> soft validation with next steps
        var midCheck = (await checkEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                mergedDraftId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        midCheck.Status.Should().Be("Draft");
        midCheck.DraftCount.Should().Be(1);
        midCheck.IsReadyToFinalise.Should().BeFalse();
        midCheck.PendingErrors.Should().NotBeEmpty();
        midCheck.NextSteps.Should().Contain(s => s.Contains("postcode", StringComparison.OrdinalIgnoreCase));

        // Chunk 2: adds site.postcode (deep-merged into site!) and updates blocks array
        var put2 = (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"site":{"postcode":"SW1P 3BT"},"blocks":["Block A","Block B"]}"""),
                mergedDraftId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        put2.IsSuccessful.Should().BeTrue();
        put2.DraftCount.Should().Be(2);
        put2.PendingErrors.Should().BeEmpty();
        put2.NextSteps.Should().ContainSingle(s => s.Contains("finalise", StringComparison.OrdinalIgnoreCase));

        // Check endpoint after chunk 2 -> merges chunk 1 + chunk 2, confirms ReadyToFinalise & gives finalise next step
        var readyCheck = (await checkEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                mergedDraftId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        readyCheck.DraftCount.Should().Be(2);
        readyCheck.IsReadyToFinalise.Should().BeTrue();
        readyCheck.PendingErrors.Should().BeEmpty();
        readyCheck.Identifier.Should().Be("104521");
        readyCheck.IdentifierType.Should().Be("URN");
        readyCheck.NextSteps.Should().ContainSingle(s => s.Contains($"/submissions/{mergedDraftId}/finalise"));

        // 3. Also keep a Draft submission with 1 chunk (unfinalised) on schemaA so stats show both Draft + Finalised
        var inProgressId = await _harness.CreateDraftSubmissionAsync(schemaA, 1);
        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":109999,"site":{"name":"Draft School"}}"""),
                inProgressId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Finalise the 2-chunk submission -> merges chunk 1 + chunk 2 into processed container!
        var finaliseRes = (await finaliseEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                mergedDraftId,
                CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);
        finaliseRes.Identifier.Should().Be("104521");
        finaliseRes.IdentifierType.Should().Be("URN");

        // Verify processed blob bundle actually contains the merged data from BOTH timestamped chunks
        var processedContainer = _harness.BlobClient.GetBlobContainerClient(_harness.ProcessedContainerName);
        var processedBlob = processedContainer.GetBlobClient($"urn=104521/schema={schemaA}/v=1/{mergedDraftId}/payload.json");
        (await processedBlob.ExistsAsync()).Value.Should().BeTrue();
        var processedContent = (await processedBlob.DownloadContentAsync()).Value.Content.ToString();
        using (var processedDoc = JsonDocument.Parse(processedContent))
        {
            processedDoc.RootElement.GetProperty("urn").GetInt32().Should().Be(104521);
            processedDoc.RootElement.GetProperty("site").GetProperty("name").GetString().Should().Be("Oak Primary");
            processedDoc.RootElement.GetProperty("site").GetProperty("postcode").GetString().Should().Be("SW1P 3BT");
            processedDoc.RootElement.GetProperty("blocks").EnumerateArray().Select(x => x.GetString())
                .Should().Equal("Block A", "Block B");
        }

        // Check endpoint after finalisation -> Finalised + locked next step
        var finalisedCheck = (await checkEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                mergedDraftId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        finalisedCheck.Status.Should().Be("Finalised");
        finalisedCheck.DraftCount.Should().Be(2);
        finalisedCheck.NextSteps.Should().ContainSingle(s => s.Contains("No further action", StringComparison.OrdinalIgnoreCase));

        // --- Admin UI Phase 3: Statistics Page ---
        var statsPage = _harness.Get<GetStatisticsPage>();
        var statsRes = await statsPage.HandleAsync(
            new HttpRequestBuilder().WithQuery("schemaName", schemaA).Build(),
            CancellationToken.None);
        var statsHtml = WebUtility.HtmlDecode(statsRes.Should().BeOfType<ContentResult>().Subject.Content!);
        statsHtml.Should().Contain("Submission Statistics");
        statsHtml.Should().Contain($"{schemaA} v1");
        statsHtml.Should().Contain($"/manage/submissions?schemaName={Uri.EscapeDataString(schemaA)}&version=1&status=Draft");
        statsHtml.Should().Contain($"/manage/submissions/{mergedDraftId}");
        statsHtml.Should().Contain($"/manage/submissions/{inProgressId}");
        statsHtml.Should().NotContain(emptyDraftId.ToString()); // Filtered to schemaA

        // --- Admin UI Phase 3: Submissions List Page ---
        var submissionsPage = _harness.Get<GetSubmissionsPage>();
        var draftFilterRes = await submissionsPage.HandleAsync(
            new HttpRequestBuilder()
                .WithQuery("schemaName", schemaA)
                .WithQuery("status", "Draft")
                .Build(),
            CancellationToken.None);
        var draftFilterHtml = WebUtility.HtmlDecode(draftFilterRes.Should().BeOfType<ContentResult>().Subject.Content!);
        draftFilterHtml.Should().Contain(inProgressId.ToString());
        draftFilterHtml.Should().NotContain(mergedDraftId.ToString());

        // --- Admin UI Phase 4 & 5: Submission Detail Inspector (Draft + Finalised + Severity + Bundle Layout) ---
        var detailPage = _harness.Get<GetSubmissionDetailPage>();

        var draftDetailRes = await detailPage.HandleAsync(HttpRequestBuilder.Empty(), inProgressId, CancellationToken.None);
        var draftDetailHtml = WebUtility.HtmlDecode(draftDetailRes.Should().BeOfType<ContentResult>().Subject.Content!);
        draftDetailHtml.Should().Contain(inProgressId.ToString());
        draftDetailHtml.Should().Contain("Validation Status & Next Steps");
        draftDetailHtml.Should().Contain("Pending Validation Issues");
        draftDetailHtml.Should().Contain("Error (");
        draftDetailHtml.Should().Contain("Current Merged Draft Preview");
        draftDetailHtml.Should().Contain("Draft Upload #1");
        draftDetailHtml.Should().Contain("Draft School");

        var finalisedDetailRes = await detailPage.HandleAsync(HttpRequestBuilder.Empty(), mergedDraftId, CancellationToken.None);
        var finalisedDetailHtml = WebUtility.HtmlDecode(finalisedDetailRes.Should().BeOfType<ContentResult>().Subject.Content!);
        finalisedDetailHtml.Should().Contain(mergedDraftId.ToString());
        finalisedDetailHtml.Should().Contain("Processed Canonical Envelope");
        finalisedDetailHtml.Should().Contain("Bundle folder:");
        finalisedDetailHtml.Should().Contain($"urn=104521/schema={schemaA}/v=1/{mergedDraftId}/");
        finalisedDetailHtml.Should().Contain("SW1P 3BT");
        finalisedDetailHtml.Should().Contain("Draft Upload #1");
        finalisedDetailHtml.Should().Contain("Draft Upload #2");
        finalisedDetailHtml.Should().Contain("Audit Trail & Processes Consumed");
        finalisedDetailHtml.Should().Contain("DraftPayloadMerger");
        finalisedDetailHtml.Should().Contain("QualityRulesValidator");
        finalisedDetailHtml.Should().Contain("BlobStorage:PromoteToProcessed");
    }
}
