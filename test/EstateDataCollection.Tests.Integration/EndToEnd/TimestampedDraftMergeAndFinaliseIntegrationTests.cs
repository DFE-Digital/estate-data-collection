using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.EndToEnd;

[Collection("AzuriteCollection")]
public sealed class TimestampedDraftMergeAndFinaliseIntegrationTests(AzuriteFixture fixture)
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
    public async Task Partial_First_Draft_Check_Should_Report_PendingErrors_And_MissingField_NextSteps()
    {
        // Arrange
        var schemaName = await SubmitAndActivateMultiChunkSchemaAsync();
        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);

        // Act
        var put1 = (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":104521,"site":{"name":"Oak Primary"},"blocks":["Block A"]}"""),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        var midCheck = (await _harness.Get<GetCheckSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);

        // Assert
        put1.IsSuccessful.Should().BeFalse();
        put1.DraftCount.Should().Be(1);
        put1.NextSteps.Should().NotBeNullOrEmpty();

        midCheck.Status.Should().Be("Draft");
        midCheck.DraftCount.Should().Be(1);
        midCheck.IsReadyToFinalise.Should().BeFalse();
        midCheck.PendingErrors.Should().NotBeEmpty();
        midCheck.NextSteps.Should().Contain(s => s.Contains("postcode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Second_Draft_Chunk_Should_DeepMerge_And_Mark_ReadyToFinalise()
    {
        // Arrange
        var schemaName = await SubmitAndActivateMultiChunkSchemaAsync();
        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);
        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":104521,"site":{"name":"Oak Primary"},"blocks":["Block A"]}"""),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Act
        var put2 = (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"site":{"postcode":"SW1P 3BT"},"blocks":["Block A","Block B"]}"""),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        var readyCheck = (await _harness.Get<GetCheckSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);

        // Assert
        put2.IsSuccessful.Should().BeTrue();
        put2.DraftCount.Should().Be(2);
        put2.PendingErrors.Should().BeEmpty();
        put2.NextSteps.Should().ContainSingle(s => s.Contains("finalise", StringComparison.OrdinalIgnoreCase));

        readyCheck.DraftCount.Should().Be(2);
        readyCheck.IsReadyToFinalise.Should().BeTrue();
        readyCheck.PendingErrors.Should().BeEmpty();
        readyCheck.Identifier.Should().Be("104521");
        readyCheck.IdentifierType.Should().Be("URN");
        readyCheck.NextSteps.Should().ContainSingle(s => s.Contains($"/submissions/{subId}/finalise"));
    }

    [Fact]
    public async Task Finalise_Merged_Draft_Should_Write_Merged_Payload_Bundle_And_Lock_Check()
    {
        // Arrange
        var schemaName = await SubmitAndActivateMultiChunkSchemaAsync();
        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);
        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":104521,"site":{"name":"Oak Primary"},"blocks":["Block A"]}"""),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"site":{"postcode":"SW1P 3BT"},"blocks":["Block A","Block B"]}"""),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Act
        var finaliseRes = (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        var finalisedCheck = (await _harness.Get<GetCheckSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);

        var processedContainer = _harness.BlobClient.GetBlobContainerClient(_harness.ProcessedContainerName);
        var processedBlob = processedContainer.GetBlobClient($"urn=104521/schema={schemaName}/v=1/{subId}/payload.json");
        var processedContent = (await processedBlob.DownloadContentAsync()).Value.Content.ToString();

        // Assert
        finaliseRes.Identifier.Should().Be("104521");
        finaliseRes.IdentifierType.Should().Be("URN");

        using var processedDoc = JsonDocument.Parse(processedContent);
        processedDoc.RootElement.GetProperty("urn").GetInt32().Should().Be(104521);
        processedDoc.RootElement.GetProperty("site").GetProperty("name").GetString().Should().Be("Oak Primary");
        processedDoc.RootElement.GetProperty("site").GetProperty("postcode").GetString().Should().Be("SW1P 3BT");
        processedDoc.RootElement.GetProperty("blocks").EnumerateArray().Select(x => x.GetString())
            .Should().Equal("Block A", "Block B");

        finalisedCheck.Status.Should().Be("Finalised");
        finalisedCheck.DraftCount.Should().Be(2);
        finalisedCheck.NextSteps.Should().ContainSingle(s => s.Contains("No further action", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> SubmitAndActivateMultiChunkSchemaAsync()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("merge_chunk");
        var rulesJson = new QualityRulesBuilder()
            .AddCelRule("CEL-URN-RANGE", "urn", "URN must be >= 100000", "urn >= 100000")
            .BuildJson();

        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(MultiChunkSchemaJson, RulesJson: rulesJson)),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);

        (await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow.AddDays(30),
                    LifecycleAction: "Activate")),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        return schemaName;
    }
}
