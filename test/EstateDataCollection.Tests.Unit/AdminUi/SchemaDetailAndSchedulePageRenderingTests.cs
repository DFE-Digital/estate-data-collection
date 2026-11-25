using System.Net;
using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class SchemaDetailAndSchedulePageRenderingTests
{
    private static RazorComponentRenderer CreateRenderer()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new RazorComponentRenderer(services, NullLoggerFactory.Instance);
    }

    private static AdminPageMeta CreateMeta(string title = "Test Page", string activeNav = "schemas") =>
        new(Title: title, ActiveNav: activeNav, SignedInUser: "jane.doe@education.gov.uk", AppVersion: "2026.11.1");

    [Fact]
    public async Task SchemaDetailPage_Should_Render_Field_Tree_Quality_Rules_Engine_Diagnostics_And_Raw_Schema()
    {
        // Arrange
        var renderer = CreateRenderer();
        var detail = new SchemaVersionDetailBuilder()
            .WithSchema("academy-return", 3)
            .WithState(SchemaLifecycleState.Scheduled)
            .WithEffectiveWindow(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero))
            .CreatedAt(new DateTimeOffset(2026, 11, 10, 8, 30, 0, TimeSpan.Zero))
            .WithField(
                name: "school",
                path: "$.school",
                type: "object",
                required: true,
                children: new SchemaFieldDescriptor(
                    Name: "urn",
                    Path: "$.school.urn",
                    Type: "integer",
                    Required: true,
                    Constraints: new SchemaFieldConstraints(Minimum: 100000, Maximum: 999999)))
            .WithQualityRule("rule-1", "URN must be valid", engine: "cel", targetPath: "$.school.urn", description: "Checks URN")
            .WithRawJsonSchema("{\"type\":\"object\"}")
            .WithIdentifier("URN", "/school/urn", required: null)
            .WithValidFixtures("""{"school":{"urn":123456}}""")
            .WithInvalidFixtures("""{"school":{"urn":0}}""")
            .WithEngineTrace("json-schema", "JsonSchema.Net Validator", ValidationEngineKinds.StructuralSchema, "Succeeded", 3, 0, 2, "Schema valid")
            .WithRegisteredEngine("json-schema", "JsonSchema.Net Validator", ValidationEngineKinds.StructuralSchema)
            .WithRegisteredEngine("cel", "Google CEL Engine", ValidationEngineKinds.QualityRule)
            .Build();
        var model = new SchemaDetailViewModel(CreateMeta("academy-return v3"), detail);

        // Act
        var html = await renderer.RenderComponentAsync<SchemaDetailPage>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });

        // Assert
        html.Should().Contain("academy-return");
        html.Should().Contain("$.school.urn");
        html.Should().Contain("min: 100000; max: 999999");
        html.Should().Contain("rule-1");
        html.Should().Contain("cel");
        html.Should().Contain("1 active rule");
        html.Should().Contain("1 top-level field");
        html.Should().Contain("Default");
        html.Should().Contain("View Stored Tests (2)");
        html.Should().Contain("/manage/schemas/academy-return/3/tests/run");
        html.Should().Contain("id=\"validation-engine-diagnostics\"");
        html.Should().Contain("id=\"registered-validation-engines\"");
        html.Should().Contain("Active (1 rule)");
        html.Should().Contain("2 of 2 active or applicable");
        html.Should().Contain("latest regression test run");
        html.Should().Contain("data-module=\"govuk-tabs\"");
        html.Should().Contain("id=\"schema-detail-tabs\"");
        html.Should().Contain("href=\"#schema-fields\"");
        html.Should().Contain("href=\"#schema-quality-rules\"");
        html.Should().Contain("href=\"#schema-validation-engines\"");
        html.Should().Contain("href=\"#schema-raw-json\"");
        WebUtility.HtmlDecode(html).Should().Contain("{\"type\":\"object\"}");
    }

    [Fact]
    public async Task ScheduleSchemaPage_Should_Render_Form_Fields_And_Validation_Error_Summary()
    {
        // Arrange
        var renderer = CreateRenderer();
        var error = Error.Validation(
            "Schema.InvalidSchedule",
            "Schedule invalid",
            [new ValidationError("effectiveToUtc", "Must be after Effective From.")]);
        var model = new ScheduleSchemaViewModel(
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
            PageError: error);

        // Act
        var scheduleHtml = await renderer.RenderComponentAsync<ScheduleSchemaPage>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });

        // Assert
        scheduleHtml.Should().Contain("There is a problem");
        scheduleHtml.Should().Contain("effectiveToUtc");
        scheduleHtml.Should().Contain("Must be after Effective From.");
        scheduleHtml.Should().Contain("action=\"/manage/schemas/school-site/1/schedule\"");
        scheduleHtml.Should().Contain("Yes (required by JSON Schema)");
        scheduleHtml.Should().Contain("id=\"identifier-pointer-group\" hidden");
        scheduleHtml.Should().Contain("LAEstab");
    }
}
