using System.Text.Json.Nodes;
using AwesomeAssertions;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Workflow;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Builders.Payloads;
using EstateDataCollection.Tests.Common.Extensions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class CcdElementalV3RuleBehaviorTests
{
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
            { "CCD-ELEM-V3-031", RuleSeverity.Error, d => d["elements"]![1]!["occurrences"]![0]!["id"] = "OCC-001" },
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
            { "CCD-ELEM-V3-045", RuleSeverity.Error, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["repairDescription"] = "" },
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
            { "CCD-ELEM-V3-051", RuleSeverity.Warning, d => d["elements"]![0]!["occurrences"]![0]!["repairs"]![0]!["repairClassification"] = "Z" },
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
        // Arrange
        var parsed = new OdcsContractParser().Parse(CcdElementalV3ContractBuilder.DefaultOdcsYaml).ShouldSucceed();
        var ambientFixtures = CcdElementalV3PayloadBuilder.BuildAmbientDictionary();
        var payload = new CcdElementalV3PayloadBuilder().Mutate(mutator).BuildJson();
        var coordinator = new QualityRuleCoordinator();

        // Act
        var result = (await coordinator.ValidateAsync(
            payload,
            parsed.ExtractedRules,
            ambientFixtures)).ShouldSucceed();

        // Assert
        result.EffectiveIssues[expectedSeverity].Should().Contain(e => e.RuleId == expectedRuleId);
    }
}
