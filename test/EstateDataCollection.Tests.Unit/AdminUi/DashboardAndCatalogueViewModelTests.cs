using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class DashboardAndCatalogueViewModelTests
{
    private static AdminPageMeta CreateMeta(string activeNav = "schemas") =>
        new("Test Title", activeNav, "user@education.gov.uk", "2026.11.1");

    [Fact]
    public void DashboardViewModel_Should_Compute_Warning_Failure_And_Engine_Trace_Summaries()
    {
        // Arrange
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

        // Act
        var vm = new DashboardViewModel(CreateMeta(), [], TestRunResult: run);

        // Assert
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
        // Arrange
        var now = new DateTimeOffset(2026, 11, 17, 14, 30, 0, TimeSpan.Zero);
        var entry = new SchemaCatalogueEntryBuilder()
            .WithName("cdc_survey")
            .WithActiveVersion(1)
            .WithVersion(v => v.WithVersion(1).WithState(SchemaLifecycleState.Active).WithEffectiveWindow(now).WithHash("hash1").CreatedAt(now).WithLastTestRun(now, "1.0", passed: true, warnings: []))
            .WithVersion(v => v.WithVersion(2).WithState(SchemaLifecycleState.Validated).WithHash("hash2").CreatedAt(now).WithLastTestRun(now, "1.0", passed: true, warnings: ["warn"]))
            .WithVersion(v => v.WithVersion(3).WithState(SchemaLifecycleState.Validated).WithHash("hash3").CreatedAt(now).WithLastTestRun(now, "1.0", passed: false))
            .WithVersion(v => v.WithVersion(4).WithState(SchemaLifecycleState.Draft).WithHash("hash4").CreatedAt(now))
            .Build();

        // Act
        var card = SchemaSectionCardViewModel.From(entry);

        // Assert
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
        // Arrange
        var err = Error.NotFound("Schema.NotFound", "Missing");

        // Act
        var vm = new SchemaVersionsViewModel(CreateMeta(), Schema: null, PageError: err);

        // Assert
        vm.HasSchema.Should().BeFalse();
        vm.SchemaCard.Should().BeNull();
        vm.ErrorSummary.Should().NotBeNull();
        vm.ErrorSummary!.Code.Should().Be("Schema.NotFound");
    }
}
