using System.Net;
using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Components;
using EstateDataCollection.Admin.UI.Models;
using EstateDataCollection.Admin.UI.Models.ViewModels;
using EstateDataCollection.Admin.UI.Pages;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Tests.Common.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class WorkbenchAndSubmissionPageRenderingTests
{
    private static RazorComponentRenderer CreateRenderer()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new RazorComponentRenderer(services, NullLoggerFactory.Instance);
    }

    private static AdminPageMeta CreateMeta(string title = "Test Page", string activeNav = "schemas") =>
        new(Title: title, ActiveNav: activeNav, SignedInUser: "jane.doe@education.gov.uk", AppVersion: "2026.11.1");

    [Fact]
    public async Task WorkbenchPage_Default_State_Should_Not_Predetermine_Identifier_Property()
    {
        // Arrange
        var renderer = CreateRenderer();
        var model = new WorkbenchViewModel(CreateMeta("Contract Workbench", activeNav: "workbench"));

        // Act
        var html = await renderer.RenderComponentAsync<WorkbenchPage>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });

        // Assert
        html.Should().NotContain("Platform default");
        html.Should().Contain("Enter JSON Pointer manually");
        WebUtility.HtmlDecode(html).Should().Contain("Not set — enter or select a property");
        html.Should().Contain("Derived from property name");
        html.Should().Contain("id=\"identifier-pointer-group\">");
    }

    [Fact]
    public async Task WorkbenchPage_With_Verification_Result_Should_Render_Diagnostics_And_Identifier_Summary()
    {
        // Arrange
        var renderer = CreateRenderer();
        var model = new WorkbenchViewModel(
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
                    ],
                    EngineTraces:
                    [
                        new ValidationEngineTrace(
                            new WorkflowEngineDescriptor("json-schema", "JsonSchema.Net Validator", ValidationEngineKinds.StructuralSchema, "Validates JSON Schema"),
                            "Succeeded",
                            2,
                            0,
                            1,
                            "Schema valid; checked 0 valid and 1 invalid fixture(s)")
                    ])));

        // Act
        var workbenchHtml = await renderer.RenderComponentAsync<WorkbenchPage>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });
        var decodedWorkbench = WebUtility.HtmlDecode(workbenchHtml);

        // Assert
        decodedWorkbench.Should().Contain("Contract Workbench");
        decodedWorkbench.Should().Contain("Verification passed");
        decodedWorkbench.Should().Contain("Schema verified successfully");
        decodedWorkbench.Should().Contain("1 invalid fixture(s) failed validation");
        decodedWorkbench.Should().Contain("Failed — unknown if expected");
        decodedWorkbench.Should().Contain("[Schema] /schoolCode: Required property missing");
        decodedWorkbench.Should().Contain("SchoolCode");
        decodedWorkbench.Should().Contain("/schoolCode");
        decodedWorkbench.Should().Contain("Required by schema:");
        decodedWorkbench.Should().Contain("id=\"validation-engine-diagnostics\"");
        decodedWorkbench.Should().Contain("JsonSchema.Net Validator");
        decodedWorkbench.Should().Contain("id=\"workbench-verify-tabs\"");
        decodedWorkbench.Should().Contain("href=\"#verify-summary\"");
        decodedWorkbench.Should().Contain("href=\"#verify-engine-diagnostics\"");
        decodedWorkbench.Should().Contain("formaction=\"/manage/workbench/verify\"");
        decodedWorkbench.Should().Contain("id=\"identifier-pointer-group\">");
    }

    [Fact]
    public async Task VerifyResultPanel_Should_Render_ErrorSummary_When_Verification_Fails()
    {
        // Arrange
        var renderer = CreateRenderer();
        var model = new WorkbenchVerifyViewModel(
            HasRun: true,
            Error: Error.Validation(
                "Schema.VerificationFailed",
                "Candidate schema failed pre-flight tests.",
                [new ValidationError("schema", "Invalid JSON Schema syntax")]));

        // Act
        var html = await renderer.RenderComponentAsync<VerifyResultPanel>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });

        // Assert
        html.Should().Contain("govuk-error-summary");
        html.Should().Contain("Schema.VerificationFailed");
        html.Should().Contain("Invalid JSON Schema syntax");
    }

    [Fact]
    public async Task SubmissionDetailPage_Should_Render_Per_Process_ErrorCode_And_Errors_In_Audit_Table()
    {
        // Arrange
        var renderer = CreateRenderer();
        var session = SubmissionSession.Create("edc_payload", 1);
        var detail = new GetSubmissionDetailResultBuilder()
            .WithSession(session)
            .WithAuditEntry(entry => entry
                .WithAction("FinaliseSubmission")
                .WithOutcome("Rejected", 422, errorCode: "Validation.Failed", message: "Validation failed")
                .WithProcess(
                    SubmissionProcessCatalog.QualityRulesValidator,
                    p => p.WithOutcome(SubmissionProcessOutcome.ValidationErrorsFound)
                        .WithDurationMs(5)
                        .WithItemCount(1)
                        .WithSummary("Evaluated 1 quality rule(s); 1 violation(s)")
                        .WithErrorCode("Validation.QualityRuleFailed")
                        .WithError("$.school_capacity", "Capacity must be positive", "RULE-CAP")))
            .Build();
        var model = new SubmissionDetailViewModel(CreateMeta("Submission", "submissions"), detail);

        // Act
        var html = await renderer.RenderComponentAsync<SubmissionDetailPage>(new Dictionary<string, object?>
        {
            ["Model"] = model
        });
        var decoded = WebUtility.HtmlDecode(html);

        // Assert
        decoded.Should().Contain("QualityRulesValidator");
        decoded.Should().Contain("Validation.QualityRuleFailed");
        decoded.Should().Contain("$.school_capacity");
        decoded.Should().Contain("[RULE-CAP] Capacity must be positive");
        decoded.Should().Contain("id=\"submission-detail-tabs\"");
        decoded.Should().Contain("href=\"#submission-audit-trail\"");
        decoded.Should().Contain("href=\"#submission-payload-session\"");
        decoded.Should().Contain("href=\"#submission-draft-uploads\"");
    }
}
