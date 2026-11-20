using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
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
        var detail = new SchemaVersionDetail(
            SchemaName: "cdc survey",
            Version: 2,
            State: SchemaLifecycleState.Active.ToNode(),
            EffectiveFromUtc: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveToUtc: null,
            IsActiveNow: true,
            CanAcceptTraffic: true,
            Sha256Hash: "abcdef1234567890",
            CreatedAtUtc: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            Fields: [],
            QualityRules:
            [
                new SchemaQualityRuleSummary("r1", "validity", "error", "", "cel", "Bad", "Desc1"),
                new SchemaQualityRuleSummary("r2", "validity", "error", "", "jsonlogic", "Bad", "Desc2"),
                new SchemaQualityRuleSummary("r3", "validity", "error", "", "CEL", "Bad", "Desc3")
            ],
            ValidFixtures: ["""{"urn":123456}"""],
            InvalidFixtures:
            [
                """{"description":"With expected","payload":{"urn":1},"expectedErrors":["RULE-1","RULE-2"]}""",
                """{"urn":2}"""
            ],
            LastTestRunAtUtc: new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero),
            LastTestRunAppVersion: "build-9",
            LastTestRunPassed: true,
            LastTestRunInvalidFixtures:
            [
                new InvalidFixtureOutcome(
                    Index: 0,
                    FailedAsExpected: false,
                    TriggeredErrors: ["[Rule:RULE-1] /urn: bad"],
                    ExpectedErrors: ["RULE-1", "RULE-2"],
                    UnmatchedExpectedErrors: ["RULE-2"],
                    Description: "With expected"),
                new InvalidFixtureOutcome(
                    Index: 1,
                    FailedAsExpected: true,
                    TriggeredErrors: ["[Schema] /urn: too small"],
                    ExpectedErrors: [],
                    UnmatchedExpectedErrors: [],
                    Warning: "unknown if expected")
            ]);

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
    public void DashboardViewModel_Should_Compute_Warning_And_Failure_States()
    {
        var run = new RunSchemaTestsResult(
            ExecutedAtUtc: DateTimeOffset.UtcNow,
            AppVersion: "v1",
            TotalSchemasChecked: 2,
            PassedCount: 2,
            FailedCount: 0,
            Results:
            [
                new SchemaTestSuiteItemResult("a", 1, "Active", true, true, 1, 1, 0, []),
                new SchemaTestSuiteItemResult("b", 2, "Validated", true, true, 0, 1, 0, [], Warnings: ["warn-1"])
            ]);

        var vm = new DashboardViewModel(CreateMeta(), [], TestRunResult: run);

        vm.TestRunSummary.Should().NotBeNull();
        vm.TestRunSummary!.SummaryTagText.Should().Be("2 / 2 Passed (warnings)");
        vm.TestRunSummary.SummaryTagCssClass.Should().Be("govuk-tag govuk-tag--yellow");
        vm.TestRunSummary.Results[0].ResultTagText.Should().Be("Passed");
        vm.TestRunSummary.Results[1].ResultTagText.Should().Be("Passed (warnings)");
        vm.TestRunSummary.Results[1].DetailsText.Should().Be("warn-1");
    }

    [Fact]
    public void SchemaSectionCardViewModel_Should_Project_All_Test_States()
    {
        var now = new DateTimeOffset(2026, 11, 17, 14, 30, 0, TimeSpan.Zero);
        var entry = new SchemaCatalogueEntry(
            SchemaName: "cdc_survey",
            ActiveVersion: 1,
            Versions:
            [
                new SchemaVersionSummary(
                    "cdc_survey", 1, SchemaLifecycleState.Active.ToNode(), now, null, true, true, "hash1", now,
                    LastTestRunAtUtc: now, LastTestRunAppVersion: "1.0", LastTestRunPassed: true, LastTestRunWarnings: []),
                new SchemaVersionSummary(
                    "cdc_survey", 2, SchemaLifecycleState.Validated.ToNode(), null, null, false, false, "hash2", now,
                    LastTestRunAtUtc: now, LastTestRunAppVersion: "1.0", LastTestRunPassed: true, LastTestRunWarnings: ["warn"]),
                new SchemaVersionSummary(
                    "cdc_survey", 3, SchemaLifecycleState.Validated.ToNode(), null, null, false, false, "hash3", now,
                    LastTestRunAtUtc: now, LastTestRunAppVersion: "1.0", LastTestRunPassed: false),
                new SchemaVersionSummary(
                    "cdc_survey", 4, SchemaLifecycleState.Draft.ToNode(), null, null, false, false, "hash4", now)
            ]);

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
    public void WorkbenchVerifyViewModel_Should_Project_Outcome_Statuses_And_Identifier()
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
                Warnings: ["SampleInvalidFixtures:1 failed validation"]));

        vm.ShowPanel.Should().BeTrue();
        vm.HasResult.Should().BeTrue();
        vm.HasWarnings.Should().BeTrue();
        vm.InvalidFixtureOutcomes[0].StatusTagText.Should().Be("Matched expected error(s)");
        vm.InvalidFixtureOutcomes[0].StatusTagCssClass.Should().Be("govuk-tag govuk-tag--green");
        vm.InvalidFixtureOutcomes[1].StatusTagText.Should().Be("Failed \u2014 unknown if expected");
        vm.InvalidFixtureOutcomes[1].StatusTagCssClass.Should().Be("govuk-tag govuk-tag--yellow");
    }
}
