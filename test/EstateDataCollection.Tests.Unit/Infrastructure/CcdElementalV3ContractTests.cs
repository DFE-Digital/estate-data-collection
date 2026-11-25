using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Application.Validators.Workflow;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Infrastructure.Validation.Enrichment;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class CcdElementalV3ContractTests
{
    private static Dictionary<string, JsonNode?> LoadAmbientFixtures()
    {
        var raw = CdeContractFixtureLoader.LoadFile("fixtures/ccd-elemental-v3.ambient.json");
        var root = JsonNode.Parse(raw)!.AsObject();
        return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["cross_submission"] = root["cross_submission"]?.DeepClone(),
            ["reference"] = root["reference"]?.DeepClone()
        };
    }

    private static string ResolveReferenceDataFixtureRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "Fixtures",
                "CdeContracts",
                "reference-data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var repoCandidate = Path.Combine(
                dir.FullName,
                "test",
                "EstateDataCollection.Tests.Common",
                "Fixtures",
                "CdeContracts",
                "reference-data");
            if (Directory.Exists(repoCandidate))
            {
                return repoCandidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Fixtures/CdeContracts/reference-data.");
    }

    [Fact]
    public void OdcsContractParser_And_ContractConsistencyAnalyzer_Should_Validate_All_57_CcdElementalV3_Rules_Cleanly()
    {
        var yaml = CdeContractFixtureLoader.LoadFile("contracts/ccd-elemental-v3.odcs.yaml");
        var parser = new OdcsContractParser();

        var parsed = parser.Parse(yaml).ShouldSucceed();
        parsed.Name.Should().Be("ccd-elemental-v3");
        parsed.ValidationPolicy.Should().NotBeNull();
        parsed.ValidationPolicy!.FailOnMinimumSeverity.Should().Be(RuleSeverity.Error);
        parsed.ValidationPolicy.CrossSubmissionDraftPolicy.Should().Be(CrossSubmissionDraftPolicy.WarnOnDraftReferences);
        parsed.ValidationPolicy.WorkflowPipelineKey.Should().BeNull();

        var expectedIds = Enumerable.Range(1, 57)
            .Select(i => $"CCD-ELEM-V3-{i:D3}")
            .ToArray();

        parsed.ExtractedRules.Should().HaveCount(57);
        parsed.ExtractedRules.Select(r => r.Id).Should().BeEquivalentTo(expectedIds);

        var coordinator = new QualityRuleCoordinator();
        var syntaxErrors = coordinator.ValidateRulesSyntax(parsed.GeneratedRulesJson!, out _).ShouldSucceed();
        syntaxErrors.Should().BeEmpty();

        var analyzer = new ContractConsistencyAnalyzer();
        var consistency = analyzer.Analyze(parsed.GeneratedSchemaJson, parsed.ExtractedRules);
        consistency.Findings.Should().BeEmpty();
        consistency.Errors.Should().BeEmpty();
        consistency.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task SchemaVerificationService_Should_Verify_CcdElementalV3_Contract_With_Valid_And_Invalid_Fixtures()
    {
        var yaml = CdeContractFixtureLoader.LoadFile("contracts/ccd-elemental-v3.odcs.yaml");
        var validPayload = CdeContractFixtureLoader.LoadFile("fixtures/ccd-elemental-v3.valid.json");
        var ambientFixtures = LoadAmbientFixtures();

        var invalidPayload1 = MutateValidPayload(doc =>
        {
            doc["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["repairItem"] = "Roof Tile";
        });
        var invalidPayload2 = MutateValidPayload(doc =>
        {
            doc["elements"]![0]!["occurrences"]![0]!["photoReference"] = null;
        });

        var invalidSpecs = new[]
        {
            JsonNode.Parse($$"""{"description":"Wrong repairItem for Boiler","payload":{{invalidPayload1}},"expectedErrors":["CCD-ELEM-V3-019"]}""")!.ToJsonString(),
            JsonNode.Parse($$"""{"description":"Grade D missing photoReference","payload":{{invalidPayload2}},"expectedErrors":["CCD-ELEM-V3-002"]}""")!.ToJsonString()
        };

        var verifier = new SchemaVerificationService(
            new QualityRuleCoordinator(),
            new OdcsContractParser(),
            Options.Create(new ValidationOptions()),
            consistencyAnalyzer: new ContractConsistencyAnalyzer());

        var result = (await verifier.VerifyCandidateSchemaAsync(
            rawSchemaJson: yaml,
            rawRulesJson: null,
            sampleValidPayloads: [validPayload],
            sampleInvalidPayloads: invalidSpecs,
            identifierConfig: null,
            cancellationToken: CancellationToken.None,
            validationPolicy: null,
            ambientFixtures: ambientFixtures)).ShouldSucceed();

        result.IsSuccessful.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.HasWarnings.Should().BeFalse();
        result.AnalyzerFindings.Should().BeEmpty();
        result.Issues[RuleSeverity.Info].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-038");
        result.InvalidFixtureOutcomes.Should().HaveCount(2);
        result.InvalidFixtureOutcomes!.Should().OnlyContain(o => o.FailedAsExpected);
    }

    [Fact]
    public async Task FullWorkflowPipeline_With_ReferenceDataProvider_And_SchemaValidationPolicy_Should_Evaluate_Valid_And_Warning_Gating()
    {
        var yaml = CdeContractFixtureLoader.LoadFile("contracts/ccd-elemental-v3.odcs.yaml");
        var validPayload = CdeContractFixtureLoader.LoadFile("fixtures/ccd-elemental-v3.valid.json");
        var parsed = new OdcsContractParser().Parse(yaml).ShouldSucceed();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var blobClient = Substitute.For<Azure.Storage.Blobs.BlobServiceClient>();
        var emptyContainer = Substitute.For<Azure.Storage.Blobs.BlobContainerClient>();
        emptyContainer.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(false, Substitute.For<Azure.Response>()));
        blobClient.GetBlobContainerClient("reference-data").Returns(emptyContainer);

        var refProvider = new BlobReferenceDataProvider(
            blobClient,
            cache,
            Options.Create(new BlobSubmissionRepositoryOptions()),
            Options.Create(new ValidationOptions { ReferenceDataLocalPath = ResolveReferenceDataFixtureRoot() }));

        var indexRepo = Substitute.For<IIdentifierIndexRepository>();
        var siblingId = Guid.NewGuid();
        var siblingEntry = new IdentifierIndexEntry(
            siblingId,
            "edc-survey-sites",
            1,
            "Finalised",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            ProcessedBundlePrefix: $"urn=104521/schema=edc-survey-sites/v=1/{siblingId}/");

        indexRepo.GetEntriesAsync("URN", "104521", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<IdentifierIndexEntry>>.Success([siblingEntry]));
        indexRepo.ReadSiblingPayloadAsync(siblingEntry, Arg.Any<CancellationToken>())
            .Returns(Result<string?>.Success("""{"sites":[{"id":"SITE-MAIN","blocks":[{"id":"BLK-A"},{"id":"BLK-B"}]}]}"""));

        var runner = ValidationWorkflowRunner.CreateDefault(
            new JsonSchemaNetValidator(),
            new QualityRuleCoordinator(),
            identifierIndexRepository: indexRepo,
            referenceDataProvider: refProvider);
        var validationService = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator(), workflowRunner: runner);

        var errorPolicySchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(parsed.GeneratedSchemaJson)
            .WithRules(parsed.ExtractedRules)
            .ConfigureMetadata(m => m
                .WithName("ccd-elemental-v3")
                .WithVersion(3)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true })
                .WithValidationPolicy(parsed.ValidationPolicy!))
            .Build();

        var report = (await validationService.ValidatePayloadWithTraceAsync(
            validPayload,
            errorPolicySchema,
            ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();

        report.IsSuccessful(RuleSeverity.Error).Should().BeTrue();
        report.Errors.Should().BeEmpty();
        report.EffectiveIssues[RuleSeverity.Info].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-038");
        report.ProcessesExecuted.Select(p => p.ProcessName).Should().ContainInOrder(
            "JsonSchemaValidator",
            "IdentifierExtractor",
            "CrossSubmissionEnrichment",
            "ReferenceDataEnrichment",
            "QualityRulesValidator");

        // Now trigger Warning-only rule CCD-ELEM-V3-028 (Grade A with score < 85)
        var warningOnlyPayload = MutateValidPayload(doc =>
        {
            doc["elements"]![1]!["occurrences"]![0]!["conditionScore"] = 72.0;
        });

        var errorGateReport = (await validationService.ValidatePayloadWithTraceAsync(
            warningOnlyPayload,
            errorPolicySchema,
            ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();
        errorGateReport.IsSuccessful(RuleSeverity.Error).Should().BeTrue();
        errorGateReport.EffectiveIssues[RuleSeverity.Warning].Should().ContainSingle(e => e.RuleId == "CCD-ELEM-V3-028");

        // Switch policy to FailOnMinimumSeverity = Warning -> Warning becomes blocking!
        var strictWarningSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(parsed.GeneratedSchemaJson)
            .WithRules(parsed.ExtractedRules)
            .ConfigureMetadata(m => m
                .WithName("ccd-elemental-v3")
                .WithVersion(3)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true })
                .WithValidationPolicy(new SchemaValidationPolicy { FailOnMinimumSeverity = RuleSeverity.Warning }))
            .Build();

        var strictReport = (await validationService.ValidatePayloadWithTraceAsync(
            warningOnlyPayload,
            strictWarningSchema,
            ValidationOperationKind.SubmissionFinalise)).ShouldSucceed();
        strictReport.IsSuccessful(RuleSeverity.Warning).Should().BeFalse();
        strictReport.Errors.Should().ContainSingle(e => e.RuleId == "CCD-ELEM-V3-028");
    }

    public static TheoryData<string, RuleSeverity, Action<JsonNode>> RuleViolationCases()
    {
        var cases = new TheoryData<string, RuleSeverity, Action<JsonNode>>
        {
            { "CCD-ELEM-V3-001", RuleSeverity.Error, d => d["elements"]![0]!["productType"] = "SolarPump" },
            { "CCD-ELEM-V3-002", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["photoReference"] = null },
            { "CCD-ELEM-V3-003", RuleSeverity.Critical, d => d["survey"]!["updatedAt"] = "2026-04-01T00:00:00Z" },
            { "CCD-ELEM-V3-004", RuleSeverity.Critical, d => d["survey"]!["participantId"] = "" },
            { "CCD-ELEM-V3-005", RuleSeverity.Critical, d => d["school"]!["urn"] = "" },
            { "CCD-ELEM-V3-006", RuleSeverity.Critical, d => d["elements"] = new JsonArray() },
            { "CCD-ELEM-V3-007", RuleSeverity.Error, d => d["elements"]![0]!["nrm3Code"] = "99.99.99" },
            { "CCD-ELEM-V3-008", RuleSeverity.Error, d => d["elements"]![0]!["siteId"] = "SITE-UNKNOWN" },
            { "CCD-ELEM-V3-009", RuleSeverity.Error, d => d["elements"]![0]!["blockId"] = "BLK-UNKNOWN" },
            {
                "CCD-ELEM-V3-010", RuleSeverity.Error, d =>
                {
                    d["elements"]![2]!["floorId"] = null;
                    d["elements"]![2]!["notAssessed"] = true;
                }
            },
            {
                "CCD-ELEM-V3-011", RuleSeverity.Error, d =>
                {
                    d["elements"]![2]!["assetType"] = "space";
                    d["elements"]![2]!["spaceId"] = null;
                }
            },
            { "CCD-ELEM-V3-012", RuleSeverity.Error, d => d["elements"]![2]!["notAssessedReason"] = null },
            {
                "CCD-ELEM-V3-013", RuleSeverity.Error, d =>
                {
                    d["elements"]![2]!["notAssessed"] = false;
                    d["elements"]![2]!["occurrences"] = new JsonArray();
                }
            },
            { "CCD-ELEM-V3-014", RuleSeverity.Error, d => d["elements"]![1]!["id"] = "ELM-001" },
            {
                "CCD-ELEM-V3-015", RuleSeverity.Warning, d =>
                {
                    d["elements"]![0]!["notAssessed"] = true;
                    d["elements"]![0]!["notAssessedReason"] = "Blocked";
                }
            },
            { "CCD-ELEM-V3-016", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["basisOfAssessment"] = null },
            { "CCD-ELEM-V3-017", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["conditionGrade"] = null },
            { "CCD-ELEM-V3-018", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["conditionDescription"] = "Bad" },
            { "CCD-ELEM-V3-019", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["repairItem"] = "Roof Tile" },
            { "CCD-ELEM-V3-020", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["repairs"] = new JsonArray() },
            {
                "CCD-ELEM-V3-021", RuleSeverity.Warning, d =>
                {
                    var occ = d["elements"]![1]!["occurrences"]![0]!;
                    occ["conditionGrade"] = "C";
                    occ["photoReference"] = "PHOTO-ROOF-C.jpg";
                    occ["repairs"] = new JsonArray();
                }
            },
            { "CCD-ELEM-V3-022", RuleSeverity.Error, d => d["elements"]![1]!["occurrences"]![0]!["priorityGrade"] = "2" },
            { "CCD-ELEM-V3-023", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["priorityGrade"] = "2" },
            {
                "CCD-ELEM-V3-024", RuleSeverity.Error, d =>
                {
                    var occ = d["elements"]![1]!["occurrences"]![0]!;
                    occ["basisOfAssessment"] = "Not Seen";
                    occ["conditionDescription"] = null;
                }
            },
            {
                "CCD-ELEM-V3-025", RuleSeverity.Warning, d =>
                {
                    var occ = d["elements"]![1]!["occurrences"]![0]!;
                    occ["hasAsbestosSuspected"] = true;
                    occ["conditionDescription"] = null;
                }
            },
            {
                "CCD-ELEM-V3-026", RuleSeverity.Error, d =>
                {
                    d["elements"]![0]!["occurrences"]![0]!["replacement"]!["replacementQuantity"] = 2;
                    d["elements"]![0]!["occurrences"]![0]!["replacement"]!["replacementCost"] = 2400.0;
                }
            },
            { "CCD-ELEM-V3-027", RuleSeverity.Error, d => d["elements"]![1]!["occurrences"]![0]!["conditionScore"] = 105.0 },
            { "CCD-ELEM-V3-028", RuleSeverity.Warning, d => d["elements"]![1]!["occurrences"]![0]!["conditionScore"] = 70.0 },
            { "CCD-ELEM-V3-029", RuleSeverity.Warning, d => d["elements"]![0]!["occurrences"]![0]!["conditionScore"] = 40.0 },
            { "CCD-ELEM-V3-030", RuleSeverity.Error, d => d["elements"]![1]!["occurrences"]![0]!["surveyLevel"] = "Site" },
            {
                "CCD-ELEM-V3-031", RuleSeverity.Error, d =>
                {
                    d["elements"]![1]!["occurrences"]![0]!["id"] = "OCC-001";
                }
            },
            {
                "CCD-ELEM-V3-032", RuleSeverity.Error, d =>
                {
                    var elms = d["elements"]!.AsArray();
                    var cloneOcc = elms[1]!["occurrences"]![0]!.DeepClone();
                    cloneOcc["id"] = "OCC-DUP-1";
                    elms[1]!["occurrences"]!.AsArray().Add(cloneOcc);
                }
            },
            { "CCD-ELEM-V3-033", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["assessedAt"] = "2026-04-01T00:00:00Z" },
            {
                "CCD-ELEM-V3-034", RuleSeverity.Warning, d =>
                {
                    var elms = d["elements"]!.AsArray();
                    var cloneOcc = elms[1]!["occurrences"]![0]!.DeepClone();
                    cloneOcc["id"] = "OCC-DUP-WARN";
                    cloneOcc["conditionGrade"] = "B";
                    cloneOcc["priorityGrade"] = "2";
                    elms[1]!["occurrences"]!.AsArray().Add(cloneOcc);
                }
            },
            { "CCD-ELEM-V3-035", RuleSeverity.Error, d => d["elements"]![1]!["occurrences"]![0]!["replacement"]!["remLife"] = -1 },
            { "CCD-ELEM-V3-036", RuleSeverity.Warning, d => d["elements"]![1]!["occurrences"]![0]!["replacement"]!["remLife"] = 120 },
            { "CCD-ELEM-V3-037", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["replacement"]!["remLife"] = 8 },
            { "CCD-ELEM-V3-038", RuleSeverity.Info, _ => { } },
            {
                "CCD-ELEM-V3-039", RuleSeverity.Error, d =>
                {
                    d["elements"]![1]!["occurrences"]![0]!["replacement"]!["replacementQuantity"] = 0.0;
                    d["elements"]![1]!["occurrences"]![0]!["replacement"]!["replacementCost"] = 0.0;
                }
            },
            {
                "CCD-ELEM-V3-040", RuleSeverity.Error, d =>
                {
                    d["elements"]![1]!["occurrences"]![0]!["replacement"]!["replacementRate"] = -5.0;
                    d["elements"]![1]!["occurrences"]![0]!["replacement"]!["replacementCost"] = -500.0;
                }
            },
            { "CCD-ELEM-V3-041", RuleSeverity.Error, d => d["elements"]![1]!["occurrences"]![0]!["replacement"]!["replacementCost"] = 9999.0 },
            { "CCD-ELEM-V3-042", RuleSeverity.Error, d => d["elements"]![1]!["occurrences"]![0]!["replacement"]!["uom"] = null },
            {
                "CCD-ELEM-V3-043", RuleSeverity.Error, d =>
                {
                    var rep2 = d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!.DeepClone();
                    d["elements"]![0]!["occurrences"]![0]!["repairs"]!.AsArray().Add(rep2);
                }
            },
            {
                "CCD-ELEM-V3-044", RuleSeverity.Error, d =>
                {
                    var rep = d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!;
                    rep["repairQuantity"] = 0.0;
                    rep["totalRepairCost"] = 0.0;
                    rep["year1Cost"] = 0.0;
                    rep["year2Cost"] = 0.0;
                }
            },
            {
                "CCD-ELEM-V3-045", RuleSeverity.Error, d =>
                {
                    var rep = d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!;
                    rep["repairDescription"] = "";
                }
            },
            { "CCD-ELEM-V3-046", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["repairItemRate"] = -1.0 },
            { "CCD-ELEM-V3-047", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["year3Cost"] = -5.0 },
            { "CCD-ELEM-V3-048", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["year5Cost"] = 10.0 },
            { "CCD-ELEM-V3-049", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["totalRepairCost"] = 0.301 },
            {
                "CCD-ELEM-V3-050", RuleSeverity.Error, d =>
                {
                    var rep = d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!;
                    rep["year1Cost"] = 0.0;
                    rep["year2Cost"] = 0.3;
                }
            },
            {
                "CCD-ELEM-V3-051", RuleSeverity.Warning, d =>
                {
                    var rep = d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!;
                    rep["repairClassification"] = "Z";
                }
            },
            { "CCD-ELEM-V3-052", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["urgentActionRequired"] = false },
            { "CCD-ELEM-V3-053", RuleSeverity.Warning, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["repairDescription"] = "Too short" },
            {
                "CCD-ELEM-V3-054", RuleSeverity.Info, d =>
                {
                    var rep = d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!;
                    rep["repairQuantity"] = 1;
                    rep["repairItemRate"] = 60000.0;
                    rep["year1Cost"] = 60000.0;
                    rep["year2Cost"] = 0.0;
                    rep["totalRepairCost"] = 60000.0;
                }
            },
            { "CCD-ELEM-V3-055", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["uom"] = "m2" },
            { "CCD-ELEM-V3-056", RuleSeverity.Error, d => d["survey"]!["surveyorEmail"] = null },
            {
                "CCD-ELEM-V3-057", RuleSeverity.Warning, d =>
                {
                    var occ = d["elements"]![1]!["occurrences"]![0]!;
                    occ["basisOfAssessment"] = "Assumed";
                    occ["conditionDescription"] = "Brief";
                }
            }
        };

        return cases;
    }

    [Theory]
    [MemberData(nameof(RuleViolationCases))]
    public async Task All_57_CcdElementalV3_Rules_Should_Fire_At_Expected_Severity(
        string expectedRuleId,
        RuleSeverity expectedSeverity,
        Action<JsonNode> mutator)
    {
        var yaml = CdeContractFixtureLoader.LoadFile("contracts/ccd-elemental-v3.odcs.yaml");
        var parsed = new OdcsContractParser().Parse(yaml).ShouldSucceed();
        var ambientFixtures = LoadAmbientFixtures();

        var payload = MutateValidPayload(mutator);
        var coordinator = new QualityRuleCoordinator();

        var result = (await coordinator.ValidateAsync(
            payload,
            parsed.ExtractedRules,
            ambientFixtures)).ShouldSucceed();

        result.EffectiveIssues[expectedSeverity].Should().Contain(e => e.RuleId == expectedRuleId);
    }

    [Fact]
    public async Task Runbook_Critical_Halt_And_DependsOn_Skip_Should_Suppress_Cascading_Noise_On_CcdElementalV3()
    {
        var yaml = CdeContractFixtureLoader.LoadFile("contracts/ccd-elemental-v3.odcs.yaml");
        var parsed = new OdcsContractParser().Parse(yaml).ShouldSucceed();
        var ambientFixtures = LoadAmbientFixtures();
        var coordinator = new QualityRuleCoordinator();

        // 1. Critical Stage 10 rule CCD-ELEM-V3-003 halts Stage 20..50
        var criticalPayload = MutateValidPayload(doc =>
        {
            doc["survey"]!["updatedAt"] = "2026-04-01T00:00:00Z";
            doc["elements"]![0]!["productType"] = "InvalidProduct";
        });
        var criticalRes = (await coordinator.ValidateAsync(criticalPayload, parsed.ExtractedRules, ambientFixtures)).ShouldSucceed();
        criticalRes.EffectiveIssues[RuleSeverity.Critical].Should().ContainSingle(e => e.RuleId == "CCD-ELEM-V3-003");
        criticalRes.EffectiveIssues[RuleSeverity.Error].Should().BeEmpty();

        // 2. Stage 20 rule CCD-ELEM-V3-001 failure skips dependent Stage 30 CCD-ELEM-V3-016 and Stage 50 CCD-ELEM-V3-019
        var depPayload = MutateValidPayload(doc =>
        {
            doc["elements"]![0]!["productType"] = "InvalidProduct";
            doc["elements"]![0]!["occurrences"]![0]!["basisOfAssessment"] = null;
        });
        var depRes = (await coordinator.ValidateAsync(depPayload, parsed.ExtractedRules, ambientFixtures)).ShouldSucceed();
        depRes.EffectiveIssues[RuleSeverity.Error].Should().Contain(e => e.RuleId == "CCD-ELEM-V3-001");
        depRes.EffectiveIssues[RuleSeverity.Error].Should().NotContain(e => e.RuleId == "CCD-ELEM-V3-016");
        depRes.EffectiveIssues[RuleSeverity.Error].Should().NotContain(e => e.RuleId == "CCD-ELEM-V3-019");
    }

    private static string MutateValidPayload(Action<JsonNode> mutate)
    {
        var raw = CdeContractFixtureLoader.LoadFile("fixtures/ccd-elemental-v3.valid.json");
        var node = JsonNode.Parse(raw)!;
        mutate(node);
        return node.ToJsonString();
    }
}
