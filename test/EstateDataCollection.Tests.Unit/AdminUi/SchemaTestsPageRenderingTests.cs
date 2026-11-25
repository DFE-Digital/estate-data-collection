using System.Net;
using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class SchemaTestsPageRenderingTests
{
    private static RazorComponentRenderer CreateRenderer()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new RazorComponentRenderer(services, NullLoggerFactory.Instance);
    }

    private static AdminPageMeta CreateMeta(string title = "Test Page", string activeNav = "schemas") =>
        new(Title: title, ActiveNav: activeNav, SignedInUser: "jane.doe@education.gov.uk", AppVersion: "2026.11.1");

    [Fact]
    public async Task SchemaTestsPage_Should_Render_Stored_Fixtures_Business_Rules_Engine_Diagnostics_And_Run_Results()
    {
        // Arrange
        var renderer = CreateRenderer();
        var runAt = new DateTimeOffset(2026, 11, 17, 10, 30, 0, TimeSpan.Zero);
        var detail = new SchemaVersionDetailBuilder()
            .WithSchema("academy-return", 2)
            .WithState(SchemaLifecycleState.Active)
            .WithQualityRule("capacity-check", "Capacity must be > 0", engine: "jsonlogic", severity: "ERROR", description: "Checks positive capacity")
            .WithIdentifier("URN", "/urn", required: true)
            .WithValidFixtures("""{"urn":123456,"capacity":50}""")
            .WithInvalidFixtures("""{"urn":123456,"capacity":-5}""")
            .WithLastTestRun(runAt, "2026.11.1+sha", passed: true, errors: [])
            .Build();

        var runResult = new RunSchemaTestsResultBuilder()
            .ExecutedAt(runAt)
            .WithAppVersion("2026.11.1+sha")
            .WithItem(
                "academy-return",
                version: 2,
                state: "Active",
                passed: true,
                hasFixtures: true,
                validFixtureCount: 1,
                invalidFixtureCount: 1,
                businessRuleCount: 1,
                identifierType: "URN",
                identifierPointer: "/urn",
                engineTraces:
                [
                    new ValidationEngineTrace(
                        new ValidationEngineDescriptor("jsonlogic", "JsonLogic Engine", ValidationEngineKinds.QualityRule, "JsonLogic"),
                        "Succeeded",
                        2,
                        0,
                        3,
                        "1 rule(s) verified")
                ])
            .Build();

        var model = new SchemaTestsViewModel(
            Page: CreateMeta("Tests - academy-return v2"),
            SchemaName: "academy-return",
            Version: 2,
            Detail: detail,
            LastRunResult: runResult);

        // Act
        var html = await renderer.RenderComponentAsync<SchemaTestsPage>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });

        // Assert
        html.Should().Contain("Run Schema Tests");
        html.Should().Contain("Schema tests passed");
        html.Should().Contain("id=\"validation-engine-diagnostics\"");
        html.Should().Contain("JsonLogic Engine");
        html.Should().Contain("Valid Fixture #1");
        html.Should().Contain("Invalid Fixture #1");
        html.Should().Contain("capacity-check");
        html.Should().Contain("jsonlogic");
        html.Should().Contain("Capacity must be &gt; 0");
        WebUtility.HtmlDecode(html).Should().Contain("2026.11.1+sha");
        WebUtility.HtmlDecode(html).Should().Contain("\"capacity\": 50");
        WebUtility.HtmlDecode(html).Should().Contain("\"capacity\": -5");
        html.Should().Contain("action=\"/manage/schemas/academy-return/2/tests\"");
        html.Should().Contain("name=\"validFixtureJson\"");
        html.Should().Contain("name=\"invalidFixtureJson\"");
        html.Should().Contain("Save &amp; Verify Tests");
        html.Should().Contain("id=\"schema-tests-tabs\"");
        html.Should().Contain("data-default-tab=\"test-engine-diagnostics\"");
        html.Should().Contain("href=\"#test-valid-fixtures\"");
        html.Should().Contain("href=\"#test-invalid-fixtures\"");
        html.Should().Contain("href=\"#test-engine-diagnostics\"");
        html.Should().Contain("href=\"#edit-schema-tests\"");
        html.Should().Contain("href=\"#test-quality-rules\"");
    }

    [Fact]
    public async Task SchemaTestsPage_Should_Highlight_Invalid_Fixture_Inputs_And_Preserve_Edits()
    {
        // Arrange
        var renderer = CreateRenderer();
        var detail = new SchemaVersionDetailBuilder()
            .WithSchema("academy-return", 2)
            .WithState(SchemaLifecycleState.Validated)
            .Build();

        var err = Error.Validation(
            "Schema.VerificationFailed",
            "Candidate schema failed pre-flight tests.",
            [new ValidationError("SampleValidFixtures:0", "SampleValidFixtures:0 violated quality rules: capacity >= 0")]);

        var model = new SchemaTestsViewModel(
            Page: CreateMeta("Tests - academy-return v2"),
            SchemaName: "academy-return",
            Version: 2,
            Detail: detail,
            PageError: err,
            ValidFixtureInput: "{\"urn\":123456,\"capacity\":-1}",
            InvalidFixtureInput: "[]");

        // Act
        var html = await renderer.RenderComponentAsync<SchemaTestsPage>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });

        // Assert
        html.Should().Contain("govuk-form-group--error");
        html.Should().Contain("govuk-textarea--error");
        html.Should().Contain("SampleValidFixtures:0 violated quality rules: capacity &gt;= 0");
        html.Should().Contain("data-default-tab=\"edit-schema-tests\"");
        WebUtility.HtmlDecode(html).Should().Contain("{\"urn\":123456,\"capacity\":-1}");
    }

    [Fact]
    public async Task SchemaTestsPage_Should_Render_Triggered_Errors_ExpectedErrors_And_SoftWarnings_For_Invalid_Fixtures()
    {
        // Arrange
        var renderer = CreateRenderer();
        var detail = new SchemaVersionDetailBuilder()
            .WithSchema("cdc_survey", 1)
            .WithState(SchemaLifecycleState.Active)
            .WithValidFixtures()
            .WithInvalidFixtures(
                """{"description":"Grade D without repairs","payload":{"urn":123456},"expectedErrors":["RULE-CONDITION-GRADE-D"]}""",
                """{"urn":123456,"unexpected":true}""",
                """{"urn":123456,"grade":"D"}""")
            .WithLastTestRun(
                new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero),
                "2026.11.1+sha",
                passed: true,
                warnings: ["SampleInvalidFixtures:2 failed validation, but unknown if expected (no expectedErrors configured)."])
            .WithInvalidFixtureOutcome(
                index: 0,
                failedAsExpected: true,
                triggeredErrors: ["[Rule:RULE-CONDITION-GRADE-D] /elements/0/occurrences/0/repairs: At least one repair must be provided."],
                expectedErrors: ["RULE-CONDITION-GRADE-D"],
                unmatchedExpectedErrors: [],
                description: "Grade D without repairs")
            .WithInvalidFixtureOutcome(
                index: 1,
                failedAsExpected: false,
                triggeredErrors: [],
                expectedErrors: [],
                unmatchedExpectedErrors: [])
            .WithInvalidFixtureOutcome(
                index: 2,
                failedAsExpected: true,
                triggeredErrors: ["[Rule:RULE-CONDITION-GRADE-D] /repairs: Missing repair."],
                expectedErrors: [],
                unmatchedExpectedErrors: [],
                warning: "Failed validation, but unknown if expected because no expectedErrors assertions were configured.")
            .Build();

        var model = new SchemaTestsViewModel(
            Page: CreateMeta("Tests - cdc_survey v1"),
            SchemaName: "cdc_survey",
            Version: 1,
            Detail: detail);

        // Act
        var html = await renderer.RenderComponentAsync<SchemaTestsPage>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });
        var decoded = WebUtility.HtmlDecode(html);

        // Assert
        decoded.Should().Contain("Grade D without repairs");
        decoded.Should().Contain("Matched expected error(s)");
        decoded.Should().Contain("Triggered errors (1)");
        decoded.Should().Contain("[Rule:RULE-CONDITION-GRADE-D]");
        decoded.Should().Contain("Passed unexpectedly");
        decoded.Should().Contain("Failed — unknown if expected");
        decoded.Should().Contain("1 invalid fixture(s) failed validation");
        decoded.Should().Contain("1 soft warning(s)");
    }
}
