using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Components;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class DashboardAndSchemaCardRenderingTests
{
    private static RazorComponentRenderer CreateRenderer()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new RazorComponentRenderer(services, NullLoggerFactory.Instance);
    }

    private static AdminPageMeta CreateMeta(string title = "Test Page", string activeNav = "schemas") =>
        new(Title: title, ActiveNav: activeNav, SignedInUser: "jane.doe@education.gov.uk", AppVersion: "2026.11.1");

    [Fact]
    public async Task DashboardPage_Should_Render_Dfe_Header_Stats_And_Schema_Versions()
    {
        // Arrange
        var renderer = CreateRenderer();
        List<SchemaCatalogueEntry> schemas =
        [
            new SchemaCatalogueEntryBuilder()
                .WithName("school-site")
                .WithActiveVersion(1)
                .WithVersion(v => v
                    .WithVersion(1)
                    .WithState(SchemaLifecycleState.Active)
                    .WithEffectiveWindow(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero))
                    .CreatedAt(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero))
                    .WithIdentifier("URN", "/urn", required: true))
                .WithVersion(v => v
                    .WithVersion(2)
                    .WithState(SchemaLifecycleState.Validated)
                    .WithHash("1234567890abcdef")
                    .CreatedAt(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero))
                    .WithIdentifier("None"))
                .Build()
        ];

        var testRunResult = new RunSchemaTestsResultBuilder()
            .ExecutedAt(new DateTimeOffset(2026, 11, 17, 11, 0, 0, TimeSpan.Zero))
            .WithAppVersion("2026.11.1+abc123")
            .WithItem(
                "school-site",
                version: 1,
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
                        new WorkflowEngineDescriptor("json-schema", "JsonSchema.Net", ValidationEngineKinds.StructuralSchema, "Schema"),
                        "Succeeded",
                        3,
                        0,
                        1)
                ])
            .Build();

        var viewModel = new DashboardViewModel(
            CreateMeta("Schema Contracts & Lifecycle"),
            schemas,
            SuccessBanner: "Saved ok",
            TestRunResult: testRunResult);

        // Act
        var html = await renderer.RenderComponentAsync<DashboardPage>(new Dictionary<string, object?>
        {
            ["Model"] = viewModel
        });

        // Assert
        html.Should().Contain("Department for Education");
        html.Should().Contain("jane.doe@education.gov.uk");
        html.Should().Contain("school-site");
        html.Should().Contain("New version(s) ready to schedule");
        html.Should().Contain("Saved ok");
        html.Should().Contain("Run All Schema Tests");
        html.Should().Contain("id=\"schema-test-run-summary\"");
        html.Should().Contain("1 / 1 Passed");
        html.Should().Contain("json-schema: Succeeded");
        html.Should().Contain("/assets/css/govuk-frontend.min.css");
        html.Should().Contain("/assets/css/dfe-branding.css");
        html.Should().Contain("govuk-frontend-supported");
        html.Should().Contain("govuk-tag--green");
        html.Should().Contain("govuk-tag--purple");
        html.Should().Contain(">Tests</th>");
        html.Should().Contain("Not run");
    }

    [Fact]
    public async Task SchemaSectionCard_EnrichedView_Should_Render_Identifier_And_Tests_Links()
    {
        // Arrange
        var renderer = CreateRenderer();
        var schema = new SchemaCatalogueEntryBuilder()
            .WithName("school-site")
            .WithActiveVersion(1)
            .WithVersion(v => v
                .WithVersion(1)
                .WithState(SchemaLifecycleState.Active)
                .WithEffectiveWindow(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero))
                .CreatedAt(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero))
                .WithIdentifier("URN", "/urn", required: true))
            .WithVersion(v => v
                .WithVersion(2)
                .WithState(SchemaLifecycleState.Validated)
                .WithHash("1234567890abcdef")
                .CreatedAt(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero))
                .WithIdentifier("None"))
            .Build();
        var model = SchemaSectionCardViewModel.From(schema, enrichedView: true);

        // Act
        var html = await renderer.RenderComponentAsync<SchemaSectionCard>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });

        // Assert
        html.Should().Contain("None (disabled)");
        html.Should().Contain("/urn");
        html.Should().Contain("/manage/schemas/school-site/1/tests");
    }

    [Fact]
    public async Task SchemaSectionCard_Should_Render_All_Test_States_And_Timestamps()
    {
        // Arrange
        var renderer = CreateRenderer();
        var runAt = new DateTimeOffset(2026, 11, 17, 14, 30, 0, TimeSpan.Zero);
        var entry = new SchemaCatalogueEntryBuilder()
            .WithName("cdc_survey")
            .WithActiveVersion(1)
            .WithVersion(v => v.WithVersion(1).WithState(SchemaLifecycleState.Active).WithEffectiveWindow(runAt).WithHash("hash1").CreatedAt(runAt).WithLastTestRun(runAt, "1.0", passed: true, warnings: []))
            .WithVersion(v => v.WithVersion(2).WithState(SchemaLifecycleState.Validated).WithHash("hash2").CreatedAt(runAt).WithLastTestRun(runAt, "1.0", passed: true, warnings: ["warn"]))
            .WithVersion(v => v.WithVersion(3).WithState(SchemaLifecycleState.Validated).WithHash("hash3").CreatedAt(runAt).WithLastTestRun(runAt, "1.0", passed: false))
            .WithVersion(v => v.WithVersion(4).WithState(SchemaLifecycleState.Draft).WithHash("hash4").CreatedAt(runAt))
            .Build();
        var model = SchemaSectionCardViewModel.From(entry);

        // Act
        var html = await renderer.RenderComponentAsync<SchemaSectionCard>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });

        // Assert
        html.Should().Contain(">Tests</th>");
        html.Should().Contain("govuk-tag--green\">Passed</strong>");
        html.Should().Contain("govuk-tag--yellow\">Passed (warnings)</strong>");
        html.Should().Contain("govuk-tag--red\">Failed</strong>");
        html.Should().Contain("govuk-tag--grey\">Not run</strong>");
        html.Should().Contain("2026-11-17 14:30 UTC");
    }
}
