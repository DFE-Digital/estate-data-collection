using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.Audit;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Application.Validators.Workflow;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Infrastructure.Validation.Enrichment;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class Phase3EnrichmentWorkflowTests
{
    [Fact]
    public async Task CrossSubmissionEnrichment_Should_Load_Siblings_And_Enforce_DraftPolicy()
    {
        var currentSubId = Guid.NewGuid();
        var draftSiblingId = Guid.NewGuid();
        var finalisedSiblingId = Guid.NewGuid();

        var indexRepo = Substitute.For<IIdentifierIndexRepository>();
        var draftSibling = new IdentifierIndexEntry(
            draftSiblingId, "site-baseline", 1, "Draft", DateTimeOffset.UtcNow.AddMinutes(-2), $"{draftSiblingId}/draft.json");
        var finalisedSibling = new IdentifierIndexEntry(
            finalisedSiblingId, "block-baseline", 1, "Finalised", DateTimeOffset.UtcNow.AddMinutes(-10),
            ProcessedBundlePrefix: $"laestab=201-3614/schema=block-baseline/v=1/{finalisedSiblingId}/");
        var selfEntry = new IdentifierIndexEntry(
            currentSubId, "elemental-survey", 1, "Draft", DateTimeOffset.UtcNow);

        indexRepo.GetEntriesAsync("LAEstab", "201-3614", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<IdentifierIndexEntry>>.Success([selfEntry, draftSibling, finalisedSibling]));

        indexRepo.ReadSiblingPayloadAsync(draftSibling, Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success("""{"sites":[{"id":"SITE-1","blocks":[{"id":"BLK-A"}]}]}"""));
        indexRepo.ReadSiblingPayloadAsync(finalisedSibling, Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success("""{"blocks":[{"id":"BLK-B"}]}"""));

        const string schemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["establishment", "siteId"],
          "properties": {
            "establishment": {
              "type": "object",
              "required": ["laestab"],
              "properties": { "laestab": { "type": "string" } }
            },
            "siteId": { "type": "string" }
          }
        }
        """;

        var rules = new List<BusinessRule>
        {
            new()
            {
                Id = "CROSS-SITE-EXISTS",
                Engine = "cel",
                Path = "",
                Severity = RuleSeverity.Error,
                Description = "Site must exist in cross-submission baseline",
                Implementation = JsonValue.Create("this.siteId in ambient.cross_submission.known_site_ids")
            }
        };

        var runner = ValidationWorkflowRunner.CreateDefault(
            new JsonSchemaNetValidator(),
            new QualityRuleCoordinator(),
            identifierIndexRepository: indexRepo);
        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator(), workflowRunner: runner);

        var blockSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .ConfigureMetadata(m => m
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab", Required = true })
                .WithValidationPolicy(new SchemaValidationPolicy { CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.BlockFinaliseOnDraftReferences }))
            .Build();

        const string payload = """{"establishment":{"laestab":"201-3614"},"siteId":"SITE-1"}""";

        // 1. On SubmissionDraft: sibling draft is NOT blocking; cross-submission rule passes cleanly (and selfEntry is excluded via scope SubmissionId)
        using (SubmissionExecutionScope.Start("DraftCheck", currentSubId))
        {
            var draftReport = (await service.ValidatePayloadWithTraceAsync(
                payload, blockSchema, ValidationOperationKind.SubmissionDraft)).ShouldSucceed();
            draftReport.Errors.Should().BeEmpty();
            var crossTrace = draftReport.EngineTraces!.Single(t => t.Descriptor.EngineId == WorkflowStepIds.CrossSubmissionEnrichment);
            crossTrace.Outcome.Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
            crossTrace.ItemsEvaluated.Should().Be(2); // draftSibling + finalisedSibling, excluding selfEntry
        }

        // 2. On SubmissionFinalise with BlockFinaliseOnDraftReferences -> emits CrossSubmission.DraftReferenceBlocked Error
        using (SubmissionExecutionScope.Start("FinaliseCheck", currentSubId))
        {
            var blockReport = (await service.ValidatePayloadWithTraceAsync(
                payload, blockSchema, ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();
            blockReport.Errors.Should().ContainSingle(e =>
                e.RuleId == "CrossSubmission.DraftReferenceBlocked" &&
                e.Severity == RuleSeverity.Error &&
                e.Message.Contains(draftSiblingId.ToString()));
        }

        // 3. With WarnOnDraftReferences + default FailOnMinimumSeverity (Error) -> warning emitted in Issues, not blocking Errors
        var warnSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .ConfigureMetadata(m => m
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab", Required = true })
                .WithValidationPolicy(new SchemaValidationPolicy
                {
                    CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.WarnOnDraftReferences,
                    FailOnMinimumSeverity = RuleSeverity.Error
                }))
            .Build();

        using (SubmissionExecutionScope.Start("WarnCheck", currentSubId))
        {
            var warnReport = (await service.ValidatePayloadWithTraceAsync(
                payload, warnSchema, ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();
            warnReport.Errors.Should().BeEmpty();
            warnReport.EffectiveIssues[RuleSeverity.Warning].Should().ContainSingle(e =>
                e.RuleId == "CrossSubmission.DraftReferenceWarning");
        }

        // 4. With WarnOnDraftReferences + FailOnMinimumSeverity = Warning -> warning IS blocking
        var strictWarnSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .ConfigureMetadata(m => m
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab", Required = true })
                .WithValidationPolicy(new SchemaValidationPolicy
                {
                    CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.WarnOnDraftReferences,
                    FailOnMinimumSeverity = RuleSeverity.Warning
                }))
            .Build();

        using (SubmissionExecutionScope.Start("StrictWarnCheck", currentSubId))
        {
            var strictWarnReport = (await service.ValidatePayloadWithTraceAsync(
                payload, strictWarnSchema, ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();
            strictWarnReport.Errors.Should().ContainSingle(e => e.RuleId == "CrossSubmission.DraftReferenceWarning");
        }

        // 5. With AllowDraftReferences -> zero draft reference issues
        var allowSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .ConfigureMetadata(m => m
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab", Required = true })
                .WithValidationPolicy(new SchemaValidationPolicy { CrossSubmissionDraftPolicy = CrossSubmissionDraftPolicy.AllowDraftReferences }))
            .Build();

        using (SubmissionExecutionScope.Start("AllowCheck", currentSubId))
        {
            var allowReport = (await service.ValidatePayloadWithTraceAsync(
                payload, allowSchema, ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();
            allowReport.Errors.Should().BeEmpty();
            allowReport.EffectiveIssues.Values.SelectMany(x => x).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ReferenceDataProvider_And_EnrichmentStep_Should_Load_Local_And_Fixture_Datasets()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"edc-refdata-{Guid.NewGuid():N}");
        var schemaDir = Path.Combine(tempRoot, "ccd-elem", "v3");
        Directory.CreateDirectory(schemaDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(schemaDir, "product-types.json"),
                """["Heating","Roofing","Electrical"]""");
            await File.WriteAllTextAsync(
                Path.Combine(schemaDir, "repair-items-by-product-type.json"),
                """{"Heating":["Boiler","Radiator"],"Roofing":["Slate","Gutter"]}""");

            using var cache = new MemoryCache(new MemoryCacheOptions());
            var blobClient = Substitute.For<Azure.Storage.Blobs.BlobServiceClient>();
            var emptyContainer = Substitute.For<Azure.Storage.Blobs.BlobContainerClient>();
            emptyContainer.ExistsAsync(Arg.Any<CancellationToken>())
                .Returns(Azure.Response.FromValue(false, Substitute.For<Azure.Response>()));
            blobClient.GetBlobContainerClient("reference-data").Returns(emptyContainer);

            var provider = new BlobReferenceDataProvider(
                blobClient,
                cache,
                Options.Create(new BlobSubmissionRepositoryOptions()),
                Options.Create(new ValidationOptions { ReferenceDataLocalPath = tempRoot }));

            const string schemaJson = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "required": ["urn", "elements"],
              "properties": {
                "urn": { "type": "integer" },
                "elements": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "required": ["productType"],
                    "properties": { "productType": { "type": "string" } }
                  }
                }
              }
            }
            """;

            var rules = new List<BusinessRule>
            {
                new()
                {
                    Id = "REF-PRODUCT-TYPE",
                    Engine = "cel",
                    Path = "elements[]",
                    Severity = RuleSeverity.Error,
                    Description = "productType must exist in reference catalog",
                    Implementation = JsonValue.Create("this.productType in ambient.reference.product_types")
                },
                new()
                {
                    Id = "REF-FIXTURE-TIER",
                    Engine = "jsonlogic",
                    Path = "elements[]",
                    Severity = RuleSeverity.Warning,
                    Description = "productType must also be permitted in fixture override",
                    Implementation = JsonNode.Parse("""{"in":[{"var":"productType"},{"var":"ambient.reference.allowed_override"}]}""")!
                }
            };

            var schema = new ResolvedSchemaBuilder()
                .ConfigureMetadata(m => m.WithName("ccd-elem").WithVersion(3).WithSchema(schemaJson))
                .WithSchemaJson(schemaJson)
                .WithRules(rules)
                .Build();

            var runner = ValidationWorkflowRunner.CreateDefault(
                new JsonSchemaNetValidator(),
                new QualityRuleCoordinator(),
                referenceDataProvider: provider);
            var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator(), workflowRunner: runner);

            var ambientFixtures = new Dictionary<string, JsonNode?>
            {
                ["reference"] = JsonNode.Parse("""{"allowed-override":["Heating"]}""")
            };

            const string validPayload = """{"urn":104521,"elements":[{"productType":"Heating"}]}""";
            var validReport = (await service.ValidatePayloadWithTraceAsync(
                validPayload, schema, ValidationOperationKind.SchemaVerify, ambientFixtures)).ShouldSucceed();

            validReport.Errors.Should().BeEmpty();
            validReport.EffectiveIssues.Values.SelectMany(x => x).Should().BeEmpty();
            validReport.EngineTraces!.Single(t => t.Descriptor.EngineId == WorkflowStepIds.ReferenceDataEnrichment)
                .Outcome.Should().Be(nameof(SubmissionProcessOutcome.Succeeded));

            const string invalidPayload = """{"urn":104521,"elements":[{"productType":"UnknownSystem"}]}""";
            var invalidReport = (await service.ValidatePayloadWithTraceAsync(
                invalidPayload, schema, ValidationOperationKind.SchemaVerify, ambientFixtures)).ShouldSucceed();

            invalidReport.Errors.Should().ContainSingle(e => e.RuleId == "REF-PRODUCT-TYPE");
            invalidReport.EffectiveIssues[RuleSeverity.Warning].Should().ContainSingle(e => e.RuleId == "REF-FIXTURE-TIER");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateSubmissionUseCase_Should_Upsert_Draft_Index_When_Identifier_Extracted()
    {
        var submissionId = Guid.NewGuid();
        var session = SubmissionSession.Create("edc_payload", 1);

        var infra = Substitute.For<ISubmissionInfrastructure>();
        infra.GetSessionAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        infra.SaveDraftPayloadAsync(submissionId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        const string schemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["establishment"],
          "properties": {
            "establishment": {
              "type": "object",
              "required": ["code"],
              "properties": { "code": { "type": "string" } }
            }
          }
        }
        """;

        var resolvedSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(schemaJson)
            .ConfigureMetadata(m => m
                .WithName("edc_payload")
                .WithSchema(schemaJson)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "EstCode", JsonPointer = "/establishment/code", Required = true }))
            .Build();

        var registry = Substitute.For<ISchemaRegistry>();
        registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolvedSchema));

        var indexRepo = Substitute.For<IIdentifierIndexRepository>();
        indexRepo.UpsertEntryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IdentifierIndexEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var validationService = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());
        var evaluationService = new SubmissionEvaluationService(infra, registry, validationService);
        var sut = new UpdateSubmissionUseCase(infra, evaluationService, identifierIndexRepository: indexRepo);

        var result = (await sut.ExecuteAsync(
            new UpdateSubmissionRequest(submissionId, """{"establishment":{"code":"SCH-42"}}"""))).ShouldSucceed();

        result.IsSuccessful.Should().BeTrue();
        await indexRepo.Received(1).UpsertEntryAsync(
            "EstCode",
            "SCH-42",
            Arg.Is<IdentifierIndexEntry>(e =>
                e.SubmissionId == submissionId &&
                e.SchemaName == "edc_payload" &&
                e.SchemaVersion == 1 &&
                e.Status == "Draft" &&
                e.PayloadPath == $"{submissionId}/draft.json"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteToProcessedAsync_Should_Upsert_Finalised_Index_With_BundlePrefix()
    {
        var submissionId = Guid.NewGuid();
        var blobServiceClient = Substitute.For<Azure.Storage.Blobs.BlobServiceClient>();
        var payloadContainer = Substitute.For<Azure.Storage.Blobs.BlobContainerClient>();
        var processedContainer = Substitute.For<Azure.Storage.Blobs.BlobContainerClient>();
        var draftBlob = Substitute.For<Azure.Storage.Blobs.BlobClient>();
        var sessionBlob = Substitute.For<Azure.Storage.Blobs.BlobClient>();
        var processedBlob = Substitute.For<Azure.Storage.Blobs.BlobClient>();

        draftBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(true, Substitute.For<Azure.Response>()));
        draftBlob.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(
                Azure.Storage.Blobs.Models.BlobsModelFactory.BlobDownloadResult(BinaryData.FromString("""{"urn":104521}""")),
                Substitute.For<Azure.Response>()));
        sessionBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(false, Substitute.For<Azure.Response>()));
        processedBlob.Uri.Returns(new Uri($"https://storage.local/processed/urn=104521/schema=edc/v=1/{submissionId}/payload.json"));

        blobServiceClient.GetBlobContainerClient("payloads").Returns(payloadContainer);
        blobServiceClient.GetBlobContainerClient("processed").Returns(processedContainer);
        payloadContainer.GetBlobClient($"{submissionId}/draft.json").Returns(draftBlob);
        payloadContainer.GetBlobClient($"{submissionId}/session.json").Returns(sessionBlob);
        processedContainer.GetBlobClient(Arg.Any<string>()).Returns(processedBlob);

        var indexRepo = Substitute.For<IIdentifierIndexRepository>();
        indexRepo.UpsertEntryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IdentifierIndexEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var infra = new BlobSubmissionInfrastructure(
            blobServiceClient,
            Options.Create(new BlobSubmissionRepositoryOptions()),
            identifierIndexRepository: indexRepo);

        (await infra.PromoteToProcessedAsync(submissionId, "URN", "104521", "edc", 1)).ShouldSucceed();

        var expectedPrefix = $"urn=104521/schema=edc/v=1/{submissionId}/";
        await indexRepo.Received(1).UpsertEntryAsync(
            "URN",
            "104521",
            Arg.Is<IdentifierIndexEntry>(e =>
                e.SubmissionId == submissionId &&
                e.SchemaName == "edc" &&
                e.SchemaVersion == 1 &&
                e.Status == "Finalised" &&
                e.PayloadPath == $"{expectedPrefix}payload.json" &&
                e.ProcessedBundlePrefix == expectedPrefix),
            Arg.Any<CancellationToken>());
    }
}
