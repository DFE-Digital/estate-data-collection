using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class ViewModelPresentationTests
{
    private static AdminPageMeta CreateMeta(string activeNav = "schemas")
    {
        return new AdminPageMeta("Test Title", activeNav, "user@education.gov.uk", "2026.11.1");
    }

    [Fact]
    public void SchemaTestsViewModel_Should_Project_Field_Errors_SoftWarnings_And_Invalid_Fixture_Cards()
    {
        var detail = new SchemaVersionDetailBuilder()
            .WithSchema("cdc survey", 2)
            .WithState(SchemaLifecycleState.Active)
            .WithEffectiveWindow(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero))
            .WithHash("abcdef1234567890")
            .WithQualityRule("r1", "Bad", engine: "cel", description: "Desc1")
            .WithQualityRule("r2", "Bad", engine: "jsonlogic", description: "Desc2")
            .WithQualityRule("r3", "Bad", engine: "CEL", description: "Desc3")
            .WithValidFixtures("""{"urn":123456}""")
            .WithInvalidFixtures(
                """{"description":"With expected","payload":{"urn":1},"expectedErrors":["RULE-1","RULE-2"]}""",
                """{"urn":2}""")
            .WithLastTestRun(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero), "build-9", passed: true)
            .WithInvalidFixtureOutcome(
                index: 0,
                failedAsExpected: false,
                triggeredErrors: ["[Rule:RULE-1] /urn: bad"],
                expectedErrors: ["RULE-1", "RULE-2"],
                unmatchedExpectedErrors: ["RULE-2"],
                description: "With expected")
            .WithInvalidFixtureOutcome(
                index: 1,
                failedAsExpected: true,
                triggeredErrors: ["[Schema] /urn: too small"],
                expectedErrors: [],
                unmatchedExpectedErrors: [],
                warning: "unknown if expected")
            .Build();

        var err = Error.Validation(
            "Schema.VerificationFailed",
            "Verification failed",
            [new ValidationError("SampleValidFixtures:0", "Valid failed")]);

        var vm = new SchemaTestsViewModel(
            Page: CreateMeta(),
            SchemaName: "cdc survey",
            Version: 2,
            Detail: detail,
            PageError: err,
            ValidFixtureInput: "raw-override");

        vm.BackToDetailsUrl.Should().Be("/manage/schemas/cdc%20survey/2/details");
        vm.RunTestsActionUrl.Should().Be("/manage/schemas/cdc%20survey/2/tests/run");
        vm.UpdateTestsActionUrl.Should().Be("/manage/schemas/cdc%20survey/2/tests");
        vm.BusinessRuleEnginesSummary.Should().Be("(cel, jsonlogic)");

        vm.ValidFixtureField.HasError.Should().BeTrue();
        vm.ValidFixtureField.EditorText.Should().Be("raw-override");
        vm.ValidFixtureField.FormGroupCssClass.Should().Contain("govuk-form-group--error");
        vm.ValidFixtureField.TextareaCssClass.Should().Contain("govuk-textarea--error");
        vm.InvalidFixtureField.HasError.Should().BeFalse();
        vm.InvalidFixtureField.EditorText.Should().Contain("\"expectedErrors\"");

        vm.ShowSoftWarningBanner.Should().BeTrue();
        vm.SoftWarningCount.Should().Be(1);
        vm.ShowLastRunSoftWarningTag.Should().BeTrue();

        vm.InvalidFixtures.Should().HaveCount(2);

        var firstCard = vm.InvalidFixtures[0];
        firstCard.StatusTagText.Should().Be("Expectation mismatch");
        firstCard.StatusTagCssClass.Should().Be("govuk-tag govuk-tag--red");
        firstCard.ShowSoftWarningCallout.Should().BeFalse();
        firstCard.ExpectedAssertions.Select(a => a.TagText).Should().Equal("Matched", "Missing");
        firstCard.FormattedPayloadJson.Should().Contain("\"urn\": 1");

        var secondCard = vm.InvalidFixtures[1];
        secondCard.StatusTagText.Should().Be("Failed \u2014 unknown if expected");
        secondCard.StatusTagCssClass.Should().Be("govuk-tag govuk-tag--yellow");
        secondCard.ShowSoftWarningCallout.Should().BeTrue();
    }

    [Fact]
    public void DashboardViewModel_Should_Compute_Warning_Failure_And_Engine_Trace_Summaries()
    {
        var run = new RunSchemaTestsResultBuilder()
            .WithAppVersion("v1")
            .WithItem(
                "a",
                version: 1,
                state: "Active",
                passed: true,
                hasFixtures: true,
                validFixtureCount: 1,
                invalidFixtureCount: 0,
                engineTraces:
                [
                    new ValidationEngineTrace(
                        new ValidationEngineDescriptor("json-schema", "JSON Schema", ValidationEngineKinds.StructuralSchema, "Schema"),
                        "Succeeded",
                        2,
                        0,
                        1),
                    new ValidationEngineTrace(
                        new ValidationEngineDescriptor("jsonlogic", "JsonLogic", ValidationEngineKinds.QualityRule, "Rules"),
                        "Skipped",
                        0,
                        0,
                        0)
                ])
            .WithItem("b", version: 2, state: "Validated", passed: true, hasFixtures: true, validFixtureCount: 0, invalidFixtureCount: 1, warnings: ["warn-1"])
            .Build();

        var vm = new DashboardViewModel(CreateMeta(), [], TestRunResult: run);

        vm.TestRunSummary.Should().NotBeNull();
        vm.TestRunSummary!.SummaryTagText.Should().Be("2 / 2 Passed (warnings)");
        vm.TestRunSummary.SummaryTagCssClass.Should().Be("govuk-tag govuk-tag--yellow");
        vm.TestRunSummary.Results[0].ResultTagText.Should().Be("Passed");
        vm.TestRunSummary.Results[0].HasEngineTracesSummary.Should().BeTrue();
        vm.TestRunSummary.Results[0].EngineTracesSummary.Should().Be("json-schema: Succeeded");
        vm.TestRunSummary.Results[1].ResultTagText.Should().Be("Passed (warnings)");
        vm.TestRunSummary.Results[1].DetailsText.Should().Be("warn-1");
        vm.TestRunSummary.Results[1].HasEngineTracesSummary.Should().BeFalse();
    }

    [Fact]
    public void SchemaSectionCardViewModel_Should_Project_All_Test_States()
    {
        var now = new DateTimeOffset(2026, 11, 17, 14, 30, 0, TimeSpan.Zero);
        var entry = new SchemaCatalogueEntryBuilder()
            .WithName("cdc_survey")
            .WithActiveVersion(1)
            .WithVersion(v => v.WithVersion(1).WithState(SchemaLifecycleState.Active).WithEffectiveWindow(now).WithHash("hash1").CreatedAt(now).WithLastTestRun(now, "1.0", passed: true, warnings: []))
            .WithVersion(v => v.WithVersion(2).WithState(SchemaLifecycleState.Validated).WithHash("hash2").CreatedAt(now).WithLastTestRun(now, "1.0", passed: true, warnings: ["warn"]))
            .WithVersion(v => v.WithVersion(3).WithState(SchemaLifecycleState.Validated).WithHash("hash3").CreatedAt(now).WithLastTestRun(now, "1.0", passed: false))
            .WithVersion(v => v.WithVersion(4).WithState(SchemaLifecycleState.Draft).WithHash("hash4").CreatedAt(now))
            .Build();

        var card = SchemaSectionCardViewModel.From(entry);

        card.Versions[0].TestStatusTagText.Should().Be("Passed");
        card.Versions[0].TestStatusTagCssClass.Should().Be("govuk-tag govuk-tag--green");
        card.Versions[0].HasTestRunTimestamp.Should().BeTrue();
        card.Versions[0].TestRunTimestampDisplay.Should().Be("2026-11-17 14:30 UTC");

        card.Versions[1].TestStatusTagText.Should().Be("Passed (warnings)");
        card.Versions[1].TestStatusTagCssClass.Should().Be("govuk-tag govuk-tag--yellow");
        card.Versions[1].HasTestRunTimestamp.Should().BeTrue();

        card.Versions[2].TestStatusTagText.Should().Be("Failed");
        card.Versions[2].TestStatusTagCssClass.Should().Be("govuk-tag govuk-tag--red");
        card.Versions[2].HasTestRunTimestamp.Should().BeTrue();

        card.Versions[3].TestStatusTagText.Should().Be("Not run");
        card.Versions[3].TestStatusTagCssClass.Should().Be("govuk-tag govuk-tag--grey");
        card.Versions[3].HasTestRunTimestamp.Should().BeFalse();
    }

    [Fact]
    public void SchemaVersionsViewModel_Should_Handle_Null_Schema_Safely()
    {
        var err = Error.NotFound("Schema.NotFound", "Missing");
        var vm = new SchemaVersionsViewModel(CreateMeta(), Schema: null, PageError: err);

        vm.HasSchema.Should().BeFalse();
        vm.SchemaCard.Should().BeNull();
        vm.ErrorSummary.Should().NotBeNull();
        vm.ErrorSummary!.Code.Should().Be("Schema.NotFound");
    }

    [Fact]
    public void IdentifierConfigFieldsetViewModel_Should_Resolve_AutoDerived_Label_And_Override()
    {
        var props = new List<SchemaIdentifierPropertyOption>
        {
            new("/establishment/laestab", "laestab", "string", true, "LAEstab")
        };

        var autoVm = IdentifierConfigFieldsetViewModel.Create(
            mode: "enabled",
            selectedProperty: "/establishment/laestab",
            pointer: "/establishment/laestab",
            label: "LAEstab",
            required: null,
            availableProperties: props);

        autoVm.IsCustomPath.Should().BeFalse();
        autoVm.HasExplicitOverride.Should().BeFalse();
        autoVm.LabelInputValue.Should().BeEmpty();
        autoVm.LabelPreview.Should().Be("LAEstab");
        autoVm.RequiredPreview.Should().Be("Yes (required by JSON Schema)");

        var customVm = IdentifierConfigFieldsetViewModel.Create(
            mode: "enabled",
            selectedProperty: IdentifierPropertyDiscovery.CustomOptionValue,
            pointer: "/custom/id",
            label: "CustomId",
            required: false,
            availableProperties: props);

        customVm.IsCustomPath.Should().BeTrue();
        customVm.HidePointerGroup.Should().BeFalse();
        customVm.HasExplicitOverride.Should().BeTrue();
        customVm.LabelInputValue.Should().Be("CustomId");
        customVm.RequiredPreview.Should().Be("No (optional in JSON Schema)");
    }

    [Fact]
    public void WorkbenchVerifyViewModel_And_SchemaDetailViewModel_Should_Project_Engine_Diagnostics()
    {
        var vm = new WorkbenchVerifyViewModel(
            HasRun: true,
            Result: new VerifySchemaResult(
                Message: "Verified",
                ResolvedIdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true },
                InvalidFixtureOutcomes:
                [
                    new InvalidFixtureOutcome(0, true, ["err-a"], ExpectedErrors: ["RULE-1"]),
                    new InvalidFixtureOutcome(1, true, ["err-b"], ExpectedErrors: [])
                ],
                Warnings: ["SampleInvalidFixtures:1 failed validation"],
                EngineTraces:
                [
                    new ValidationEngineTrace(
                        new ValidationEngineDescriptor("json-schema", "JsonSchema.Net Validator", ValidationEngineKinds.StructuralSchema, "Validates structure"),
                        "Succeeded",
                        3,
                        0,
                        2,
                        "Schema valid")
                ]));

        vm.ShowPanel.Should().BeTrue();
        vm.HasResult.Should().BeTrue();
        vm.HasWarnings.Should().BeTrue();
        vm.InvalidFixtureOutcomes[0].StatusTagText.Should().Be("Matched expected error(s)");
        vm.InvalidFixtureOutcomes[0].StatusTagCssClass.Should().Be("govuk-tag govuk-tag--green");
        vm.InvalidFixtureOutcomes[1].StatusTagText.Should().Be("Failed \u2014 unknown if expected");
        vm.InvalidFixtureOutcomes[1].StatusTagCssClass.Should().Be("govuk-tag govuk-tag--yellow");
        vm.EngineDiagnostics.HasTraces.Should().BeTrue();
        vm.EngineDiagnostics.TraceRows.Should().ContainSingle(r => r.EngineId == "json-schema" && r.KindLabel == "Structural Schema");

        var detail = new SchemaVersionDetailBuilder()
            .WithQualityRule("r1", "Bad", engine: "cel")
            .WithIdentifier("None")
            .WithRegisteredEngine("json-schema", "JsonSchema.Net Validator", ValidationEngineKinds.StructuralSchema)
            .WithRegisteredEngine("cel", "Google CEL Engine", ValidationEngineKinds.QualityRule)
            .WithRegisteredEngine("jsonlogic", "JsonLogic Engine", ValidationEngineKinds.QualityRule)
            .WithRegisteredEngine("identifier-extractor", "Submission Identifier Extractor", ValidationEngineKinds.IdentifierExtraction)
            .Build();

        var detailVm = new SchemaDetailViewModel(CreateMeta(), detail);
        detailVm.EngineDiagnostics.HasRegisteredEngines.Should().BeTrue();
        detailVm.EngineDiagnostics.RegisteredEngines.Select(e => e.ApplicabilityText)
            .Should().Equal("Applicable", "Active (1 rule)", "Idle (0 rules)", "Disabled");
        detailVm.EngineDiagnostics.HasRegisteredSubtitle.Should().BeTrue();
        detailVm.EngineDiagnostics.RegisteredSubtitle.Should().Contain("2 of 4 active or applicable");

        vm.ShowTabs.Should().BeTrue();
        vm.Tabs.Tabs.Select(t => t.Id).Should().Equal("verify-summary", "verify-engine-diagnostics");
        vm.SummaryTab!.IsSelected.Should().BeTrue();
        vm.EngineDiagnosticsTab!.IsSelected.Should().BeFalse();

        detailVm.Tabs.Tabs.Select(t => t.Id)
            .Should().Equal("schema-fields", "schema-quality-rules", "schema-validation-engines");
        detailVm.FieldsTab!.IsSelected.Should().BeTrue();
        detailVm.RawJsonSchemaTab.Should().BeNull();
    }

    [Fact]
    public void GovUkTabsViewModel_And_SchemaTestsViewModel_Should_Resolve_Visible_And_Default_Tabs()
    {
        var tabs = GovUkTabsViewModel.Create(
            "sample-tabs",
            [
                ("tab-a", "First", true),
                ("tab-hidden", "Hidden", false),
                ("tab-b", "Second", true)
            ],
            preferredSelectedId: "tab-b");

        tabs.Tabs.Should().HaveCount(2);
        tabs.DefaultTabId.Should().Be("tab-b");
        tabs.Find("tab-hidden").Should().BeNull();
        tabs.Find("tab-a")!.IsSelected.Should().BeFalse();
        tabs.Find("tab-a")!.PanelCssClass.Should().Be("govuk-tabs__panel govuk-tabs__panel--hidden");
        tabs.Find("tab-b")!.IsSelected.Should().BeTrue();
        tabs.Find("tab-b")!.ListItemCssClass.Should().Be("govuk-tabs__list-item govuk-tabs__list-item--selected");
        tabs.Find("tab-b")!.PanelCssClass.Should().Be("govuk-tabs__panel");

        var detail = new SchemaVersionDetailBuilder()
            .WithSchema("cdc_survey", 1)
            .WithState(SchemaLifecycleState.Active)
            .WithEngineTrace("json-schema", "JsonSchema.Net", ValidationEngineKinds.StructuralSchema, "Succeeded", 1, 0, 1)
            .Build();

        var defaultVm = new SchemaTestsViewModel(CreateMeta(), "cdc_survey", 1, detail);
        defaultVm.Tabs.DefaultTabId.Should().BeNull();
        defaultVm.ValidFixturesTab!.IsSelected.Should().BeTrue();

        var postRunResult = new RunSchemaTestsResultBuilder()
            .WithItem(
                "cdc_survey",
                1,
                "Active",
                passed: true,
                engineTraces:
                [
                    new ValidationEngineTrace(
                        new ValidationEngineDescriptor("json-schema", "JsonSchema.Net", ValidationEngineKinds.StructuralSchema, "Schema"),
                        "Succeeded",
                        1,
                        0,
                        1)
                ])
            .Build();
        var postRunVm = new SchemaTestsViewModel(CreateMeta(), "cdc_survey", 1, detail, LastRunResult: postRunResult);
        postRunVm.Tabs.DefaultTabId.Should().Be("test-engine-diagnostics");
        postRunVm.EngineDiagnosticsTab!.IsSelected.Should().BeTrue();

        var errorVm = new SchemaTestsViewModel(
            CreateMeta(),
            "cdc_survey",
            1,
            detail,
            PageError: Error.Validation("Schema.InvalidFixture", "Invalid", [new ValidationError("validFixtureJson", "Bad JSON")]));
        errorVm.Tabs.DefaultTabId.Should().Be("edit-schema-tests");
        errorVm.EditTestsTab!.IsSelected.Should().BeTrue();
    }
}
