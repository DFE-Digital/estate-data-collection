using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class WorkbenchAndSchemaDetailViewModelTests
{
    private static AdminPageMeta CreateMeta(string activeNav = "schemas") =>
        new("Test Title", activeNav, "user@education.gov.uk", "2026.11.1");

    [Fact]
    public void IdentifierConfigFieldsetViewModel_When_DiscoveredPropertySelected_Should_Derive_Label_Without_Override()
    {
        // Arrange
        List<SchemaIdentifierPropertyOption> props =
        [
            new("/establishment/laestab", "laestab", "string", true, "LAEstab")
        ];

        // Act
        var autoVm = IdentifierConfigFieldsetViewModel.Create(
            mode: "enabled",
            selectedProperty: "/establishment/laestab",
            pointer: "/establishment/laestab",
            label: "LAEstab",
            required: null,
            availableProperties: props);

        // Assert
        autoVm.IsCustomPath.Should().BeFalse();
        autoVm.HasExplicitOverride.Should().BeFalse();
        autoVm.LabelInputValue.Should().BeEmpty();
        autoVm.LabelPreview.Should().Be("LAEstab");
        autoVm.RequiredPreview.Should().Be("Yes (required by JSON Schema)");
    }

    [Fact]
    public void IdentifierConfigFieldsetViewModel_When_CustomPathSelected_Should_Expose_Override_And_Pointer_Group()
    {
        // Arrange
        List<SchemaIdentifierPropertyOption> props =
        [
            new("/establishment/laestab", "laestab", "string", true, "LAEstab")
        ];

        // Act
        var customVm = IdentifierConfigFieldsetViewModel.Create(
            mode: "enabled",
            selectedProperty: IdentifierPropertyDiscovery.CustomOptionValue,
            pointer: "/custom/id",
            label: "CustomId",
            required: false,
            availableProperties: props);

        // Assert
        customVm.IsCustomPath.Should().BeTrue();
        customVm.HidePointerGroup.Should().BeFalse();
        customVm.HasExplicitOverride.Should().BeTrue();
        customVm.LabelInputValue.Should().Be("CustomId");
        customVm.RequiredPreview.Should().Be("No (optional in JSON Schema)");
    }

    [Fact]
    public void WorkbenchVerifyViewModel_Should_Project_Outcome_Tags_And_Engine_Diagnostics()
    {
        // Arrange
        var result = new VerifySchemaResult(
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
            ]);

        // Act
        var vm = new WorkbenchVerifyViewModel(HasRun: true, Result: result);

        // Assert
        vm.ShowPanel.Should().BeTrue();
        vm.HasResult.Should().BeTrue();
        vm.HasWarnings.Should().BeTrue();
        vm.InvalidFixtureOutcomes[0].StatusTagText.Should().Be("Matched expected error(s)");
        vm.InvalidFixtureOutcomes[0].StatusTagCssClass.Should().Be("govuk-tag govuk-tag--green");
        vm.InvalidFixtureOutcomes[1].StatusTagText.Should().Be("Failed \u2014 unknown if expected");
        vm.InvalidFixtureOutcomes[1].StatusTagCssClass.Should().Be("govuk-tag govuk-tag--yellow");
        vm.EngineDiagnostics.HasTraces.Should().BeTrue();
        vm.EngineDiagnostics.TraceRows.Should().ContainSingle(r => r.EngineId == "json-schema" && r.KindLabel == "Structural Schema");
        vm.ShowTabs.Should().BeTrue();
        vm.Tabs.Tabs.Select(t => t.Id).Should().Equal("verify-summary", "verify-engine-diagnostics");
        vm.SummaryTab!.IsSelected.Should().BeTrue();
        vm.EngineDiagnosticsTab!.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void SchemaDetailViewModel_Should_Project_Engine_Applicability_And_Tabs()
    {
        // Arrange
        var detail = new SchemaVersionDetailBuilder()
            .WithQualityRule("r1", "Bad", engine: "cel")
            .WithIdentifier("None")
            .WithRegisteredEngine("json-schema", "JsonSchema.Net Validator", ValidationEngineKinds.StructuralSchema)
            .WithRegisteredEngine("cel", "Google CEL Engine", ValidationEngineKinds.QualityRule)
            .WithRegisteredEngine("jsonlogic", "JsonLogic Engine", ValidationEngineKinds.QualityRule)
            .WithRegisteredEngine("identifier-extractor", "Submission Identifier Extractor", ValidationEngineKinds.IdentifierExtraction)
            .Build();

        // Act
        var detailVm = new SchemaDetailViewModel(CreateMeta(), detail);

        // Assert
        detailVm.EngineDiagnostics.HasRegisteredEngines.Should().BeTrue();
        detailVm.EngineDiagnostics.RegisteredEngines.Select(e => e.ApplicabilityText)
            .Should().Equal("Applicable", "Active (1 rule)", "Idle (0 rules)", "Disabled");
        detailVm.EngineDiagnostics.HasRegisteredSubtitle.Should().BeTrue();
        detailVm.EngineDiagnostics.RegisteredSubtitle.Should().Contain("2 of 4 active or applicable");
        detailVm.Tabs.Tabs.Select(t => t.Id)
            .Should().Equal("schema-fields", "schema-quality-rules", "schema-validation-engines");
        detailVm.FieldsTab!.IsSelected.Should().BeTrue();
        detailVm.RawJsonSchemaTab.Should().BeNull();
    }

    [Fact]
    public void GovUkTabsViewModel_Should_Filter_Hidden_Tabs_And_Apply_Preferred_Selection()
    {
        // Arrange
        (string Id, string Label, bool Visible)[] definitions =
        [
            ("tab-a", "First", true),
            ("tab-hidden", "Hidden", false),
            ("tab-b", "Second", true)
        ];

        // Act
        var tabs = GovUkTabsViewModel.Create("sample-tabs", definitions, preferredSelectedId: "tab-b");

        // Assert
        tabs.Tabs.Should().HaveCount(2);
        tabs.DefaultTabId.Should().Be("tab-b");
        tabs.Find("tab-hidden").Should().BeNull();
        tabs.Find("tab-a")!.IsSelected.Should().BeFalse();
        tabs.Find("tab-a")!.PanelCssClass.Should().Be("govuk-tabs__panel govuk-tabs__panel--hidden");
        tabs.Find("tab-b")!.IsSelected.Should().BeTrue();
        tabs.Find("tab-b")!.ListItemCssClass.Should().Be("govuk-tabs__list-item govuk-tabs__list-item--selected");
        tabs.Find("tab-b")!.PanelCssClass.Should().Be("govuk-tabs__panel");
    }
}
