using System.Text.Json;
using AwesomeAssertions;
using Azure.Storage.Blobs.Models;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Public;

[Collection("AzuriteCollection")]
public sealed class SubmissionAuditTrailIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    private const string CapacitySchemaJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["urn", "capacity"],
      "properties": {
        "urn": { "type": "integer", "minimum": 100000 },
        "capacity": { "type": "integer", "minimum": 0 }
      }
    }
    """;

    [Fact]
    public async Task Submission_Create_Update_Check_And_RejectedFinalise_Should_Persist_Chronological_Audit_Blobs()
    {
        // Arrange
        var schemaName = await SubmitAndActivateCapacitySchemaAsync();
        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();
        var statusEndpoint = _harness.Get<GetSubmissionStatusEndpoint>();
        var finaliseEndpoint = _harness.Get<PostFinaliseSubmissionEndpoint>();

        // Act
        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);
        (await updateEndpoint.HandleAsync(HttpRequestBuilder.FromRaw("""{"urn":104521,"capacity":4}"""), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);
        (await statusEndpoint.HandleAsync(HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        (await finaliseEndpoint.HandleAsync(HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        var entries = (await _harness.Get<ISubmissionAuditStore>().ListAsync(subId)).ShouldSucceed();
        var payloadsContainer = _harness.BlobClient.GetBlobContainerClient(_harness.PayloadsContainerName);
        var auditBlobNames = await ListBlobNamesAsync(payloadsContainer, $"{subId}/audit/");

        // Assert
        auditBlobNames.Should().HaveCount(4);
        auditBlobNames.Should().OnlyContain(name => name.StartsWith($"{subId}/audit/") && name.EndsWith(".json"));

        var firstRaw = (await payloadsContainer.GetBlobClient(auditBlobNames[0]).DownloadContentAsync()).Value.Content.ToString();
        using var doc = JsonDocument.Parse(firstRaw);
        doc.RootElement.TryGetProperty("processesConsumed", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("outcome", out _).Should().BeTrue();

        entries.Select(e => e.Action).Should().Equal(
            "CreateSubmission",
            "UpdateSubmission",
            "CheckSubmissionStatus",
            "FinaliseSubmission");

        entries[0].Outcome.Status.Should().Be("Succeeded");
        entries[0].Outcome.HttpStatus.Should().Be(201);
        entries[0].ProcessesConsumed.Select(p => p.ProcessName).Should().Contain(["SchemaResolution", "BlobStorage:CreateSession"]);

        entries[1].Outcome.Status.Should().Be("SavedWithPendingErrors");
        entries[1].Outcome.ValidationErrorCount.Should().Be(1);
        entries[1].ProcessesConsumed.Should().Contain(p =>
            p.ProcessName == "QualityRulesValidator" &&
            p.Engine != null &&
            p.Engine.Contains("cel", StringComparison.OrdinalIgnoreCase));

        entries[3].Outcome.Status.Should().Be("Rejected");
        entries[3].Outcome.HttpStatus.Should().Be(422);
        entries[3].Outcome.ErrorCode.Should().Be("Schema.ContractViolation");
    }

    [Fact]
    public async Task Submission_Finalisation_And_Second_Attempt_Should_Record_Audit_And_Mirror_Bundle()
    {
        // Arrange
        var schemaName = await SubmitAndActivateCapacitySchemaAsync();
        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);
        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();
        var statusEndpoint = _harness.Get<GetSubmissionStatusEndpoint>();
        var finaliseEndpoint = _harness.Get<PostFinaliseSubmissionEndpoint>();

        (await updateEndpoint.HandleAsync(HttpRequestBuilder.FromRaw("""{"urn":104521,"capacity":50}"""), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);
        (await statusEndpoint.HandleAsync(HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Act
        (await finaliseEndpoint.HandleAsync(HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);
        (await finaliseEndpoint.HandleAsync(HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status403Forbidden);

        var entries = (await _harness.Get<ISubmissionAuditStore>().ListAsync(subId)).ShouldSucceed();
        var bundlePrefix = $"urn=104521/schema={schemaName}/v=1/{subId}/";
        var processedContainer = _harness.BlobClient.GetBlobContainerClient(_harness.ProcessedContainerName);
        var bundleBlobs = await ListBlobNamesAsync(processedContainer, bundlePrefix);
        var bundleSessionRaw = (await processedContainer.GetBlobClient($"{bundlePrefix}session.json").DownloadContentAsync()).Value.Content.ToString();
        var bundleSession = JsonSerializer.Deserialize<SubmissionSession>(bundleSessionRaw);

        // Assert
        entries.Select(e => e.Action).Should().Equal(
            "CreateSubmission",
            "UpdateSubmission",
            "CheckSubmissionStatus",
            "FinaliseSubmission",
            "FinaliseSubmission");

        entries[2].Outcome.Status.Should().Be("ReadyToFinalise");
        entries[2].Identifier?.Type.Should().Be("URN");
        entries[2].Identifier?.Value.Should().Be("104521");

        entries[3].Outcome.Status.Should().Be("Succeeded");
        entries[3].Outcome.HttpStatus.Should().Be(200);
        entries[3].ProcessesConsumed.Should().Contain(p =>
            p.ProcessName == "BlobStorage:PromoteToProcessed" &&
            p.Outcome == SubmissionProcessOutcome.Succeeded);
        entries[3].Details.Should().ContainKey("processedBlobUri");

        entries[4].Outcome.Status.Should().Be("Rejected");
        entries[4].Outcome.HttpStatus.Should().Be(403);
        entries[4].Outcome.ErrorCode.Should().Be("Submission.AlreadyFinalised");

        bundleBlobs.Should().Contain($"{bundlePrefix}payload.json");
        bundleBlobs.Should().Contain($"{bundlePrefix}session.json");
        bundleBlobs.Where(b => b.StartsWith($"{bundlePrefix}audit/", StringComparison.Ordinal) && b.EndsWith(".json", StringComparison.Ordinal))
            .Should().HaveCount(5);

        bundleSession.Should().NotBeNull();
        bundleSession!.IsFinalised.Should().BeTrue();
        bundleSession.ProcessedBundlePrefix.Should().Be(bundlePrefix);
    }

    private async Task<string> SubmitAndActivateCapacitySchemaAsync()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("audit_trail");
        var rulesJson = new QualityRulesBuilder()
            .AddCelRule("CEL-MIN-CAP", "capacity", "Capacity must be at least 10", "capacity >= 10")
            .BuildJson();

        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: CapacitySchemaJson,
                    RulesJson: rulesJson)),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);

        (await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                    EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                    EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30),
                    LifecycleAction: "Activate")),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        return schemaName;
    }

    private static async Task<List<string>> ListBlobNamesAsync(Azure.Storage.Blobs.BlobContainerClient container, string prefix)
    {
        var names = new List<string>();
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: prefix, cancellationToken: CancellationToken.None))
        {
            names.Add(blob.Name);
        }

        return names;
    }
}
