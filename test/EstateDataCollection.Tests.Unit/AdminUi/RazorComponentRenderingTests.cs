using System.Net;
using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Assets;
using EstateDataCollection.Admin.UI.Components;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class RazorComponentRenderingTests
{
    private static RazorComponentRenderer CreateRenderer()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new RazorComponentRenderer(services, NullLoggerFactory.Instance);
    }

    private static AdminPageMeta CreateMeta(string title = "Test Page", string activeNav = "schemas")
    {
        return new AdminPageMeta(
            Title: title,
            ActiveNav: activeNav,
            SignedInUser: "jane.doe@education.gov.uk",
            AppVersion: "2026.11.1");
    }

    [Fact]
    public async Task DashboardPage_Should_Render_Dfe_Header_Stats_And_Schema_Versions()
    {
        var renderer = CreateRenderer();
        var schemas = new List<SchemaCatalogueEntry>
        {
            new(
                SchemaName: "school-site",
                ActiveVersion: 1,
                Versions:
                [
                    new SchemaVersionSummary(
                        SchemaName: "school-site",
                        Version: 1,
                        State: SchemaLifecycleState.Active.ToNode(),
                        EffectiveFromUtc: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                        EffectiveToUtc: null,
                        IsActiveNow: true,
                        CanAcceptTraffic: true,
                        Sha256Hash: "abcdef1234567890",
                        CreatedAtUtc: new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero),
                        IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true }),
                    new SchemaVersionSummary(
                        SchemaName: "school-site",
                        Version: 2,
                        State: SchemaLifecycleState.Validated.ToNode(),
                        EffectiveFromUtc: null,
                        EffectiveToUtc: null,
                        IsActiveNow: false,
                        CanAcceptTraffic: false,
                        Sha256Hash: "1234567890abcdef",
                        CreatedAtUtc: new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero),
                        IdentifierConfig: new SchemaIdentifierConfig { Type = "None" })
                ])
        };

        var testRunResult = new RunSchemaTestsResult(
            ExecutedAtUtc: new DateTimeOffset(2026, 11, 17, 11, 0, 0, TimeSpan.Zero),
            AppVersion: "2026.11.1+abc123",
            TotalSchemasChecked: 1,
            PassedCount: 1,
            FailedCount: 0,
            Results:
            [
                new SchemaTestSuiteItemResult("school-site", 1, "Active", true, true, 1, 1, 1, [], "URN", "/urn")
            ]);

        var html = await renderer.RenderComponentAsync<DashboardPage>(new Dictionary<string, object?>
        {
            ["Model"] = new DashboardViewModel(
                CreateMeta("Schema Contracts & Lifecycle"),
                schemas,
                SuccessBanner: "Saved ok",
                TestRunResult: testRunResult)
        });

        html.Should().Contain("Department for Education");
        html.Should().Contain("jane.doe@education.gov.uk");
        html.Should().Contain("school-site");
        html.Should().Contain("New version(s) ready to schedule");
        html.Should().Contain("Saved ok");
        html.Should().Contain("Run All Schema Tests");
        html.Should().Contain("id=\"schema-test-run-summary\"");
        html.Should().Contain("1 / 1 Passed");
        html.Should().Contain("/assets/css/govuk-frontend.min.css");
        html.Should().Contain("/assets/css/dfe-branding.css");
        html.Should().Contain("govuk-tag--green");
        html.Should().Contain("govuk-tag--purple");
        html.Should().Contain(">Tests</th>");
        html.Should().Contain("Not run");

        var enrichedCardHtml = await renderer.RenderComponentAsync<SchemaSectionCard>(new Dictionary<string, object?>
        {
            ["Model"] = SchemaSectionCardViewModel.From(schemas[0], enrichedView: true)
        });
        enrichedCardHtml.Should().Contain("None (disabled)");
        enrichedCardHtml.Should().Contain("/urn");
        enrichedCardHtml.Should().Contain("/manage/schemas/school-site/1/tests");
    }

    [Fact]
    public async Task SchemaSectionCard_Should_Render_All_Test_States_And_Timestamps()
    {
        var renderer = CreateRenderer();
        var runAt = new DateTimeOffset(2026, 11, 17, 14, 30, 0, TimeSpan.Zero);
        var entry = new SchemaCatalogueEntry(
            SchemaName: "cdc_survey",
            ActiveVersion: 1,
            Versions:
            [
                new SchemaVersionSummary(
                    "cdc_survey", 1, SchemaLifecycleState.Active.ToNode(), runAt, null, true, true, "hash1", runAt,
                    LastTestRunAtUtc: runAt, LastTestRunAppVersion: "1.0", LastTestRunPassed: true, LastTestRunWarnings: []),
                new SchemaVersionSummary(
                    "cdc_survey", 2, SchemaLifecycleState.Validated.ToNode(), null, null, false, false, "hash2", runAt,
                    LastTestRunAtUtc: runAt, LastTestRunAppVersion: "1.0", LastTestRunPassed: true, LastTestRunWarnings: ["warn"]),
                new SchemaVersionSummary(
                    "cdc_survey", 3, SchemaLifecycleState.Validated.ToNode(), null, null, false, false, "hash3", runAt,
                    LastTestRunAtUtc: runAt, LastTestRunAppVersion: "1.0", LastTestRunPassed: false),
                new SchemaVersionSummary(
                    "cdc_survey", 4, SchemaLifecycleState.Draft.ToNode(), null, null, false, false, "hash4", runAt)
            ]);

        var html = await renderer.RenderComponentAsync<SchemaSectionCard>(new Dictionary<string, object?>
        {
            ["Model"] = SchemaSectionCardViewModel.From(entry)
        });

        html.Should().Contain(">Tests</th>");
        html.Should().Contain("govuk-tag--green\">Passed</strong>");
        html.Should().Contain("govuk-tag--yellow\">Passed (warnings)</strong>");
        html.Should().Contain("govuk-tag--red\">Failed</strong>");
        html.Should().Contain("govuk-tag--grey\">Not run</strong>");
        html.Should().Contain("2026-11-17 14:30 UTC");
    }

    [Fact]
    public async Task SchemaDetailPage_Should_Render_Field_Tree_Quality_Rules_And_Raw_Schema()
    {
        var renderer = CreateRenderer();
        var detail = new SchemaVersionDetail(
            SchemaName: "academy-return",
            Version: 3,
            State: SchemaLifecycleState.Scheduled.ToNode(),
            EffectiveFromUtc: new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveToUtc: null,
            IsActiveNow: false,
            CanAcceptTraffic: true,
            Sha256Hash: "deadbeef12345678",
            CreatedAtUtc: new DateTimeOffset(2026, 11, 10, 8, 30, 0, TimeSpan.Zero),
            Fields:
            [
                new SchemaFieldDescriptor(
                    Name: "school",
                    Path: "$.school",
                    Type: "object",
                    Required: true,
                    Children:
                    [
                        new SchemaFieldDescriptor(
                            Name: "urn",
                            Path: "$.school.urn",
                            Type: "integer",
                            Required: true,
                            Constraints: new SchemaFieldConstraints(Minimum: 100000, Maximum: 999999))
                    ])
            ],
            QualityRules:
            [
                new SchemaQualityRuleSummary(
                    Id: "rule-1",
                    Dimension: "validity",
                    Severity: "error",
                    TargetPath: "$.school.urn",
                    Engine: "cel",
                    ErrorMessage: "URN must be valid",
                    Description: "Checks URN")
            ],
            RawJsonSchema: "{\"type\":\"object\"}",
            IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = null },
            ValidFixtures: ["""{"school":{"urn":123456}}"""],
            InvalidFixtures: ["""{"school":{"urn":0}}"""]);

        var html = await renderer.RenderComponentAsync<SchemaDetailPage>(new Dictionary<string, object?>
        {
            ["Model"] = new SchemaDetailViewModel(CreateMeta("academy-return v3"), detail)
        });

        html.Should().Contain("academy-return");
        html.Should().Contain("$.school.urn");
        html.Should().Contain("min: 100000; max: 999999");
        html.Should().Contain("rule-1");
        html.Should().Contain("cel");
        html.Should().Contain("Default");
        html.Should().Contain("View Stored Tests (2)");
        html.Should().Contain("/manage/schemas/academy-return/3/tests/run");
        WebUtility.HtmlDecode(html).Should().Contain("{\"type\":\"object\"}");
    }

    [Fact]
    public async Task SchemaTestsPage_Should_Render_Stored_Fixtures_Business_Rules_And_Run_Results()
    {
        var renderer = CreateRenderer();
        var detail = new SchemaVersionDetail(
            SchemaName: "academy-return",
            Version: 2,
            State: SchemaLifecycleState.Active.ToNode(),
            EffectiveFromUtc: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveToUtc: null,
            IsActiveNow: true,
            CanAcceptTraffic: true,
            Sha256Hash: "deadbeef12345678",
            CreatedAtUtc: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            Fields: [],
            QualityRules:
            [
                new SchemaQualityRuleSummary(
                    Id: "capacity-check",
                    Dimension: "validity",
                    Severity: "ERROR",
                    TargetPath: "",
                    Engine: "jsonlogic",
                    ErrorMessage: "Capacity must be > 0",
                    Description: "Checks positive capacity")
            ],
            IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true },
            ValidFixtures: ["""{"urn":123456,"capacity":50}"""],
            InvalidFixtures: ["""{"urn":123456,"capacity":-5}"""],
            LastTestRunAtUtc: new DateTimeOffset(2026, 11, 17, 10, 30, 0, TimeSpan.Zero),
            LastTestRunAppVersion: "2026.11.1+sha",
            LastTestRunPassed: true,
            LastTestRunErrors: []);

        var runResult = new RunSchemaTestsResult(
            ExecutedAtUtc: new DateTimeOffset(2026, 11, 17, 10, 30, 0, TimeSpan.Zero),
            AppVersion: "2026.11.1+sha",
            TotalSchemasChecked: 1,
            PassedCount: 1,
            FailedCount: 0,
            Results:
            [
                new SchemaTestSuiteItemResult("academy-return", 2, "Active", true, true, 1, 1, 1, [], "URN", "/urn")
            ]);

        var html = await renderer.RenderComponentAsync<SchemaTestsPage>(new Dictionary<string, object?>
        {
            ["Model"] = new SchemaTestsViewModel(
                Page: CreateMeta("Tests - academy-return v2"),
                SchemaName: "academy-return",
                Version: 2,
                Detail: detail,
                LastRunResult: runResult)
        });

        html.Should().Contain("Run Schema Tests");
        html.Should().Contain("Schema tests passed");
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
    }

    [Fact]
    public async Task SchemaTestsPage_Should_Highlight_Invalid_Fixture_Inputs_And_Preserve_Edits()
    {
        var renderer = CreateRenderer();
        var detail = new SchemaVersionDetail(
            SchemaName: "academy-return",
            Version: 2,
            State: SchemaLifecycleState.Validated.ToNode(),
            EffectiveFromUtc: null,
            EffectiveToUtc: null,
            IsActiveNow: false,
            CanAcceptTraffic: false,
            Sha256Hash: "deadbeef12345678",
            CreatedAtUtc: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            Fields: [],
            QualityRules: []);

        var err = Error.Validation(
            "Schema.VerificationFailed",
            "Candidate schema failed pre-flight tests.",
            [new ValidationError("SampleValidFixtures:0", "SampleValidFixtures:0 violated quality rules: capacity >= 0")]);

        var html = await renderer.RenderComponentAsync<SchemaTestsPage>(new Dictionary<string, object?>
        {
            ["Model"] = new SchemaTestsViewModel(
                Page: CreateMeta("Tests - academy-return v2"),
                SchemaName: "academy-return",
                Version: 2,
                Detail: detail,
                PageError: err,
                ValidFixtureInput: "{\"urn\":123456,\"capacity\":-1}",
                InvalidFixtureInput: "[]")
        });

        html.Should().Contain("govuk-form-group--error");
        html.Should().Contain("govuk-textarea--error");
        html.Should().Contain("SampleValidFixtures:0 violated quality rules: capacity &gt;= 0");
        WebUtility.HtmlDecode(html).Should().Contain("{\"urn\":123456,\"capacity\":-1}");
    }

    [Fact]
    public async Task SchemaTestsPage_Should_Render_Triggered_Errors_ExpectedErrors_And_SoftWarnings_For_Invalid_Fixtures()
    {
        var renderer = CreateRenderer();
        var detail = new SchemaVersionDetail(
            SchemaName: "cdc_survey",
            Version: 1,
            State: SchemaLifecycleState.Active.ToNode(),
            EffectiveFromUtc: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveToUtc: null,
            IsActiveNow: true,
            CanAcceptTraffic: true,
            Sha256Hash: "deadbeef12345678",
            CreatedAtUtc: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            Fields: [],
            QualityRules: [],
            ValidFixtures: [],
            InvalidFixtures:
            [
                """{"description":"Grade D without repairs","payload":{"urn":123456},"expectedErrors":["RULE-CONDITION-GRADE-D"]}""",
                """{"urn":123456,"unexpected":true}""",
                """{"urn":123456,"grade":"D"}"""
            ],
            LastTestRunAtUtc: new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero),
            LastTestRunAppVersion: "2026.11.1+sha",
            LastTestRunPassed: true,
            LastTestRunInvalidFixtures:
            [
                new InvalidFixtureOutcome(
                    Index: 0,
                    FailedAsExpected: true,
                    TriggeredErrors: ["[Rule:RULE-CONDITION-GRADE-D] /elements/0/occurrences/0/repairs: At least one repair must be provided."],
                    ExpectedErrors: ["RULE-CONDITION-GRADE-D"],
                    UnmatchedExpectedErrors: [],
                    Description: "Grade D without repairs"),
                new InvalidFixtureOutcome(
                    Index: 1,
                    FailedAsExpected: false,
                    TriggeredErrors: [],
                    ExpectedErrors: [],
                    UnmatchedExpectedErrors: []),
                new InvalidFixtureOutcome(
                    Index: 2,
                    FailedAsExpected: true,
                    TriggeredErrors: ["[Rule:RULE-CONDITION-GRADE-D] /repairs: Missing repair."],
                    ExpectedErrors: [],
                    UnmatchedExpectedErrors: [],
                    Warning: "Failed validation, but unknown if expected because no expectedErrors assertions were configured.")
            ],
            LastTestRunWarnings:
            [
                "SampleInvalidFixtures:2 failed validation, but unknown if expected (no expectedErrors configured)."
            ]);

        var html = await renderer.RenderComponentAsync<SchemaTestsPage>(new Dictionary<string, object?>
        {
            ["Model"] = new SchemaTestsViewModel(
                Page: CreateMeta("Tests - cdc_survey v1"),
                SchemaName: "cdc_survey",
                Version: 1,
                Detail: detail)
        });

        var decoded = WebUtility.HtmlDecode(html);
        decoded.Should().Contain("Grade D without repairs");
        decoded.Should().Contain("Matched expected error(s)");
        decoded.Should().Contain("Triggered errors (1)");
        decoded.Should().Contain("[Rule:RULE-CONDITION-GRADE-D]");
        decoded.Should().Contain("Passed unexpectedly");
        decoded.Should().Contain("Failed — unknown if expected");
        decoded.Should().Contain("Soft warning: 1 invalid fixture(s) failed validation");
        decoded.Should().Contain("1 soft warning(s)");
    }

    [Fact]
    public async Task WorkbenchPage_Default_State_Should_Not_Predetermine_Identifier_Property()
    {
        var renderer = CreateRenderer();
        var html = await renderer.RenderComponentAsync<WorkbenchPage>(new Dictionary<string, object?>
        {
            ["Model"] = new WorkbenchViewModel(CreateMeta("Contract Workbench", activeNav: "workbench"))
        });

        html.Should().NotContain("Platform default");
        html.Should().Contain("Enter JSON Pointer manually");
        WebUtility.HtmlDecode(html).Should().Contain("Not set — enter or select a property");
        html.Should().Contain("Derived from property name");
        html.Should().Contain("id=\"identifier-pointer-group\">");
    }

    [Fact]
    public async Task ScheduleSchemaPage_And_WorkbenchPage_Should_Render_Forms_And_Errors()
    {
        var renderer = CreateRenderer();
        var error = Error.Validation(
            "Schema.InvalidSchedule",
            "Schedule invalid",
            [new ValidationError("effectiveToUtc", "Must be after Effective From.")]);

        var scheduleHtml = await renderer.RenderComponentAsync<ScheduleSchemaPage>(new Dictionary<string, object?>
        {
            ["Model"] = new ScheduleSchemaViewModel(
                Page: CreateMeta("Schedule"),
                SchemaName: "school-site",
                Version: 1,
                Detail: null,
                SelectedAction: "Activate",
                EffectiveFromInput: "2026-11-16T09:00",
                IdentifierMode: "enabled",
                IdentifierProperty: "/establishment/laestab",
                IdentifierPointer: "/establishment/laestab",
                IdentifierLabel: "LAEstab",
                IdentifierRequired: true,
                AvailableIdentifierProperties:
                [
                    new SchemaIdentifierPropertyOption("/establishment/laestab", "laestab", "string", true, "LAEstab")
                ],
                PageError: error)
        });

        scheduleHtml.Should().Contain("There is a problem");
        scheduleHtml.Should().Contain("effectiveToUtc");
        scheduleHtml.Should().Contain("Must be after Effective From.");
        scheduleHtml.Should().Contain("action=\"/manage/schemas/school-site/1/schedule\"");
        scheduleHtml.Should().Contain("Yes (required by JSON Schema)");
        scheduleHtml.Should().Contain("id=\"identifier-pointer-group\" hidden");
        scheduleHtml.Should().Contain("LAEstab");

        var workbenchHtml = await renderer.RenderComponentAsync<WorkbenchPage>(new Dictionary<string, object?>
        {
            ["Model"] = new WorkbenchViewModel(
                Page: CreateMeta("Contract Workbench", activeNav: "workbench"),
                InputMode: "json",
                SchemaName: "test-schema",
                Version: 2,
                IdentifierMode: "enabled",
                IdentifierProperty: IdentifierPropertyDiscovery.CustomOptionValue,
                IdentifierPointer: "/schoolCode",
                IdentifierLabel: "SchoolCode",
                Verification: new WorkbenchVerifyViewModel(
                    HasRun: true,
                    Result: new VerifySchemaResult(
                        "Schema verified successfully",
                        ResolvedIdentifierConfig: new SchemaIdentifierConfig
                        {
                            Type = "SchoolCode",
                            JsonPointer = "/schoolCode",
                            Required = true
                        },
                        InvalidFixtureOutcomes:
                        [
                            new InvalidFixtureOutcome(
                                Index: 0,
                                FailedAsExpected: true,
                                TriggeredErrors: ["[Schema] /schoolCode: Required property missing"])
                        ],
                        Warnings:
                        [
                            "SampleInvalidFixtures:0 failed validation, but unknown if expected."
                        ])))
        });

        var decodedWorkbench = WebUtility.HtmlDecode(workbenchHtml);
        decodedWorkbench.Should().Contain("Contract Workbench");
        decodedWorkbench.Should().Contain("Verification passed");
        decodedWorkbench.Should().Contain("Schema verified successfully");
        decodedWorkbench.Should().Contain("Soft warning: 1 invalid fixture(s) failed validation");
        decodedWorkbench.Should().Contain("Failed — unknown if expected");
        decodedWorkbench.Should().Contain("[Schema] /schoolCode: Required property missing");
        decodedWorkbench.Should().Contain("SchoolCode");
        decodedWorkbench.Should().Contain("/schoolCode");
        decodedWorkbench.Should().Contain("Required by schema:");
        decodedWorkbench.Should().Contain("formaction=\"/manage/workbench/verify\"");
        decodedWorkbench.Should().Contain("id=\"identifier-pointer-group\">");
    }

    [Fact]
    public async Task VerifyResultPanel_Should_Render_ErrorSummary_When_Verification_Fails()
    {
        var renderer = CreateRenderer();
        var html = await renderer.RenderComponentAsync<VerifyResultPanel>(new Dictionary<string, object?>
        {
            ["Model"] = new WorkbenchVerifyViewModel(
                HasRun: true,
                Error: Error.Validation(
                    "Schema.VerificationFailed",
                    "Candidate schema failed pre-flight tests.",
                    [new ValidationError("schema", "Invalid JSON Schema syntax")]))
        });

        html.Should().Contain("govuk-error-summary");
        html.Should().Contain("Schema.VerificationFailed");
        html.Should().Contain("Invalid JSON Schema syntax");
    }

    [Theory]
    [InlineData("css/govuk-frontend.min.css", "text/css; charset=utf-8")]
    [InlineData("css/dfe-branding.css", "text/css; charset=utf-8")]
    [InlineData("js/govuk-frontend.min.js", "application/javascript; charset=utf-8")]
    [InlineData("js/workbench.js", "application/javascript; charset=utf-8")]
    [InlineData("images/favicon.ico", "image/x-icon")]
    [InlineData("images/govuk-crest.svg", "image/svg+xml")]
    [InlineData("fonts/bold-b542beb274-v2.woff2", "font/woff2")]
    public void EmbeddedUiAssetProvider_Should_Resolve_All_Assets_Including_GovUk_Asset_Subfolders(
        string path,
        string expectedContentType)
    {
        var found = EmbeddedUiAssetProvider.TryReadAsset(path, out var bytes, out var contentType);

        found.Should().BeTrue();
        bytes.Length.Should().BeGreaterThan(0);
        contentType.Should().Be(expectedContentType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../secret.txt")]
    [InlineData("css/non-existent.css")]
    public void EmbeddedUiAssetProvider_Should_Reject_Missing_Or_Traversal_Paths(string path)
    {
        var found = EmbeddedUiAssetProvider.TryReadAsset(path, out var bytes, out _);

        found.Should().BeFalse();
        bytes.Should().BeEmpty();
    }
}
