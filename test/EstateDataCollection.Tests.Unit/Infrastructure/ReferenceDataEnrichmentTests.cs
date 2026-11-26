using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Application.Validators.Workflow;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Infrastructure.Workflow.Enrichment;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Builders.Payloads;
using EstateDataCollection.Tests.Common.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class ReferenceDataEnrichmentTests
{
    private const string SchemaJson = """
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

    private static ResolvedSchema BuildSchema()
    {
        IReadOnlyList<BusinessRule> rules =
        [
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
        ];

        return new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("ccd-elem").WithVersion(3).WithSchema(SchemaJson))
            .WithSchemaJson(SchemaJson)
            .WithRules(rules)
            .Build();
    }

    private static (PayloadValidationService Service, MemoryCache Cache) CreateService(string referenceRoot)
    {
        new CcdElementalV3ReferenceDataBuilder()
            .WithProductTypes("Heating", "Roofing", "Electrical")
            .WriteToDirectory(referenceRoot, Path.Combine("ccd-elem", "v3"));

        var cache = new MemoryCache(new MemoryCacheOptions());
        var blobClient = Substitute.For<Azure.Storage.Blobs.BlobServiceClient>();
        var emptyContainer = Substitute.For<Azure.Storage.Blobs.BlobContainerClient>();
        emptyContainer.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(false, Substitute.For<Azure.Response>()));
        blobClient.GetBlobContainerClient("reference-data").Returns(emptyContainer);

        var provider = new BlobReferenceDataProvider(
            blobClient,
            cache,
            Options.Create(new BlobSubmissionRepositoryOptions()),
            Options.Create(new ValidationOptions { ReferenceDataLocalPath = referenceRoot }));

        var runner = WorkflowRunner.CreateDefault(
            new JsonSchemaNetValidator(),
            new QualityRuleCoordinator(),
            referenceDataProvider: provider);

        var service = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator(), workflowRunner: runner);
        return (service, cache);
    }

    [Fact]
    public async Task ReferenceDataProvider_Should_Load_Local_And_Fixture_Datasets_For_Valid_Payload()
    {
        // Arrange
        using var scope = new TempDirectoryScope();
        var (service, cache) = CreateService(scope.Path);
        using var _ = cache;

        var schema = BuildSchema();
        var ambientFixtures = new Dictionary<string, JsonNode?>
        {
            ["reference"] = JsonNode.Parse("""{"allowed-override":["Heating"]}""")
        };
        const string validPayload = """{"urn":104521,"elements":[{"productType":"Heating"}]}""";

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            validPayload,
            schema,
            WorkflowOperationKind.SchemaVerify,
            ambientFixtures)).ShouldSucceed();

        // Assert
        report.Errors.Should().BeEmpty();
        report.EffectiveIssues.Values.SelectMany(x => x).Should().BeEmpty();
        report.EngineTraces!.Single(t => t.Descriptor.EngineId == WorkflowStepIds.ReferenceDataEnrichment)
            .Outcome.Should().Be(nameof(SubmissionProcessOutcome.Succeeded));
    }

    [Fact]
    public async Task ReferenceDataProvider_Should_Reject_Unknown_ProductType_Against_Merged_Datasets()
    {
        // Arrange
        using var scope = new TempDirectoryScope();
        var (service, cache) = CreateService(scope.Path);
        using var _ = cache;

        var schema = BuildSchema();
        var ambientFixtures = new Dictionary<string, JsonNode?>
        {
            ["reference"] = JsonNode.Parse("""{"allowed-override":["Heating"]}""")
        };
        const string invalidPayload = """{"urn":104521,"elements":[{"productType":"UnknownSystem"}]}""";

        // Act
        var report = (await service.ValidatePayloadWithTraceAsync(
            invalidPayload,
            schema,
            WorkflowOperationKind.SchemaVerify,
            ambientFixtures)).ShouldSucceed();

        // Assert
        report.Errors.Should().ContainSingle(e => e.RuleId == "REF-PRODUCT-TYPE");
        report.EffectiveIssues[RuleSeverity.Warning].Should().ContainSingle(e => e.RuleId == "REF-FIXTURE-TIER");
    }
}
