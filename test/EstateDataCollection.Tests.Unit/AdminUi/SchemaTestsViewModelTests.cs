using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class SchemaTestsViewModelTests
{
    private static AdminPageMeta CreateMeta(string activeNav = "schemas") =>
        new("Test Title", activeNav, "user@education.gov.uk", "2026.11.1");

    [Fact]
    public void SchemaTestsViewModel_Should_Project_Field_Errors_SoftWarnings_And_Invalid_Fixture_Cards()
    {
        // Arrange
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

        // Act
        var vm = new SchemaTestsViewModel(
            Page: CreateMeta(),
            SchemaName: "cdc survey",
            Version: 2,
            Detail: detail,
            PageError: err,
            ValidFixtureInput: "raw-override");

        // Assert
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
    public void SchemaTestsViewModel_Should_Select_ValidFixtures_Tab_By_Default()
    {
        // Arrange
        var detail = new SchemaVersionDetailBuilder()
            .WithSchema("cdc_survey", 1)
            .WithState(SchemaLifecycleState.Active)
            .WithEngineTrace("json-schema", "JsonSchema.Net", ValidationEngineKinds.StructuralSchema, "Succeeded", 1, 0, 1)
            .Build();

        // Act
        var defaultVm = new SchemaTestsViewModel(CreateMeta(), "cdc_survey", 1, detail);

        // Assert
        defaultVm.Tabs.DefaultTabId.Should().BeNull();
        defaultVm.ValidFixturesTab!.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void SchemaTestsViewModel_Should_Select_EngineDiagnostics_Tab_After_TestRun()
    {
        // Arrange
        var detail = new SchemaVersionDetailBuilder()
            .WithSchema("cdc_survey", 1)
            .WithState(SchemaLifecycleState.Active)
            .Build();
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

        // Act
        var postRunVm = new SchemaTestsViewModel(CreateMeta(), "cdc_survey", 1, detail, LastRunResult: postRunResult);

        // Assert
        postRunVm.Tabs.DefaultTabId.Should().Be("test-engine-diagnostics");
        postRunVm.EngineDiagnosticsTab!.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void SchemaTestsViewModel_Should_Select_EditTests_Tab_When_Validation_Error_Occurs()
    {
        // Arrange
        var detail = new SchemaVersionDetailBuilder()
            .WithSchema("cdc_survey", 1)
            .WithState(SchemaLifecycleState.Active)
            .Build();
        var pageError = Error.Validation(
            "Schema.InvalidFixture",
            "Invalid",
            [new ValidationError("validFixtureJson", "Bad JSON")]);

        // Act
        var errorVm = new SchemaTestsViewModel(CreateMeta(), "cdc_survey", 1, detail, PageError: pageError);

        // Assert
        errorVm.Tabs.DefaultTabId.Should().Be("edit-schema-tests");
        errorVm.EditTestsTab!.IsSelected.Should().BeTrue();
    }
}
