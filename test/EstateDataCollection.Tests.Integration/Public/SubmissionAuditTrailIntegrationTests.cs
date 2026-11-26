using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Admin;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Security;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Public;

[Collection("AzuriteCollection")]
public sealed class SubmissionAuditTrailIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Every_Submission_Interaction_Should_Create_Timestamped_Audit_Json_Blobs_With_Outcomes_And_Processes()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("audit_trail");
        const string schemaJson = """
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

        var rulesJson = new QualityRulesBuilder()
            .AddCelRule("CEL-MIN-CAP", "capacity", "Capacity must be at least 10", "capacity >= 10")
            .BuildJson();

        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: schemaJson,
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

        // 1. Create submission
        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);

        var updateEndpoint = _harness.Get<PutUpdateSubmissionEndpoint>();
        var statusEndpoint = _harness.Get<GetSubmissionStatusEndpoint>();
        var finaliseEndpoint = _harness.Get<PostFinaliseSubmissionEndpoint>();

        // 2. Update draft breaking CEL quality rule
        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":104521,"capacity":4}"""),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // 3. Check status -> pending error
        var badStatus = (await statusEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        badStatus.IsReadyToFinalise.Should().BeFalse();

        // 4. Finalise prematurely -> 422 rejected
        (await finaliseEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        // 5. Update valid draft
        (await updateEndpoint.HandleAsync(
                HttpRequestBuilder.FromRaw("""{"urn":104521,"capacity":50}"""),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // 6. Check status -> ready to finalise
        var readyStatus = (await statusEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        readyStatus.IsReadyToFinalise.Should().BeTrue();

        // 7. Finalise successfully -> promoted
        (await finaliseEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        // 8. Second finalise -> 403 rejected
        (await finaliseEndpoint.HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status403Forbidden);

        // Verify physical audit blobs in Azurite payloads/{subId}/audit/ folder
        var payloadsContainer = _harness.BlobClient.GetBlobContainerClient(_harness.PayloadsContainerName);
        var auditBlobNames = new List<string>();
        await foreach (var blob in payloadsContainer.GetBlobsAsync(traits: Azure.Storage.Blobs.Models.BlobTraits.None, states: Azure.Storage.Blobs.Models.BlobStates.None, prefix: $"{subId}/audit/", cancellationToken: CancellationToken.None))
        {
            auditBlobNames.Add(blob.Name);
        }

        auditBlobNames.Should().HaveCount(8);
        auditBlobNames.Should().OnlyContain(name => name.StartsWith($"{subId}/audit/") && name.EndsWith(".json"));

        // Verify raw JSON has camelCase structure readable by tools
        var firstRaw = (await payloadsContainer.GetBlobClient(auditBlobNames[0]).DownloadContentAsync()).Value.Content.ToString();
        using var doc = JsonDocument.Parse(firstRaw);
        doc.RootElement.TryGetProperty("processesConsumed", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("outcome", out _).Should().BeTrue();

        // Verify ISubmissionAuditStore.ListAsync returns chronological audit entries
        var entries = (await _harness.Get<ISubmissionAuditStore>().ListAsync(subId)).ShouldSucceed();
        entries.Should().HaveCount(8);
        entries.Select(e => e.Action).Should().Equal(
            "CreateSubmission",
            "UpdateSubmission",
            "CheckSubmissionStatus",
            "FinaliseSubmission",
            "UpdateSubmission",
            "CheckSubmissionStatus",
            "FinaliseSubmission",
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

        entries[5].Outcome.Status.Should().Be("ReadyToFinalise");
        entries[5].Identifier?.Type.Should().Be("URN");
        entries[5].Identifier?.Value.Should().Be("104521");

        entries[6].Outcome.Status.Should().Be("Succeeded");
        entries[6].Outcome.HttpStatus.Should().Be(200);
        entries[6].ProcessesConsumed.Should().Contain(p =>
            p.ProcessName == "BlobStorage:PromoteToProcessed" &&
            p.Outcome == SubmissionProcessOutcome.Succeeded);
        entries[6].Details.Should().ContainKey("processedBlobUri");

        entries[7].Outcome.Status.Should().Be("Rejected");
        entries[7].Outcome.HttpStatus.Should().Be(403);
        entries[7].Outcome.ErrorCode.Should().Be("Submission.AlreadyFinalised");
    }

    [Fact]
    public async Task Authenticated_ActorContext_Should_Persist_In_Azurite_Audit_Blobs_And_Render_In_Admin_Inspector()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("actor_audit");
        const string schemaJson = """
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

        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: schemaJson)),
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

        var actorContext = _harness.Get<ActorContext>();
        actorContext.SetActor(
            actorId: "sub-surveyor-01",
            authenticationType: "FaUApiSubscriptionKey",
            actorName: "Northern Estates Survey",
            subscriptionId: "sub-surveyor-01",
            subscriptionName: "Northern Estates Survey",
            metadata: new Dictionary<string, string>
            {
                ["surveyorOrg"] = "DfE North"
            });

        Guid subId;
        try
        {
            subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);

            (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                    HttpRequestBuilder.FromRaw("""{"urn":104521,"capacity":50}"""),
                    subId,
                    CancellationToken.None))
                .ShouldHaveStatusCode(StatusCodes.Status200OK);
        }
        finally
        {
            actorContext.Reset();
        }

        // Verify physical audit blob JSON in Azurite payloads/{subId}/audit/
        var payloadsContainer = _harness.BlobClient.GetBlobContainerClient(_harness.PayloadsContainerName);
        var auditBlobNames = new List<string>();
        await foreach (var blob in payloadsContainer.GetBlobsAsync(
                           traits: Azure.Storage.Blobs.Models.BlobTraits.None,
                           states: Azure.Storage.Blobs.Models.BlobStates.None,
                           prefix: $"{subId}/audit/",
                           cancellationToken: CancellationToken.None))
        {
            auditBlobNames.Add(blob.Name);
        }

        auditBlobNames.Should().HaveCount(2);
        var firstRaw = (await payloadsContainer.GetBlobClient(auditBlobNames[0]).DownloadContentAsync()).Value.Content.ToString();
        using var doc = JsonDocument.Parse(firstRaw);
        doc.RootElement.GetProperty("actor").GetString().Should().Be("Northern Estates Survey (sub-surveyor-01)");
        doc.RootElement.GetProperty("details").GetProperty("actor.id").GetString().Should().Be("sub-surveyor-01");
        doc.RootElement.GetProperty("details").GetProperty("actor.authType").GetString().Should().Be("FaUApiSubscriptionKey");
        doc.RootElement.GetProperty("details").GetProperty("actor.meta.surveyorOrg").GetString().Should().Be("DfE North");

        // Verify Admin UI Submission Detail Inspector displays the Actor on audit cards
        var detailPage = _harness.Get<GetSubmissionDetailPage>();
        var detailRes = await detailPage.HandleAsync(HttpRequestBuilder.Empty(), subId, CancellationToken.None);
        var detailHtml = WebUtility.HtmlDecode(detailRes.Should().BeOfType<ContentResult>().Subject.Content!);
        detailHtml.Should().Contain("Northern Estates Survey (sub-surveyor-01)");
    }
}
