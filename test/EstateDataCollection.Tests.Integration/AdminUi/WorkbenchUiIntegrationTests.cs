using System.Net;
using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.AzureFunctions.Admin.Routes.Admin;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EstateDataCollection.Tests.Integration.AdminUi;

[Collection("AzuriteCollection")]
public sealed class WorkbenchUiIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    private const string LaestabSchemaJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["establishment"],
      "properties": {
        "establishment": {
          "type": "object",
          "required": ["laestab"],
          "properties": {
            "laestab": { "type": "string" }
          }
        }
      }
    }
    """;

    [Fact]
    public async Task GetWorkbenchPage_Should_Render_IdentifierAgnostic_Prompt()
    {
        // Arrange
        var request = HttpRequestBuilder.Empty();

        // Act
        var result = await _harness.Get<GetWorkbenchPage>().HandleAsync(request);

        // Assert
        var html = result.Should().BeOfType<ContentResult>().Subject;
        html.StatusCode.Should().Be(StatusCodes.Status200OK);
        html.Content.Should().Contain("Contract Workbench");
        html.Content.Should().NotContain("Platform default");
        WebUtility.HtmlDecode(html.Content).Should().Contain("Not set — enter or select a property");
        html.Content.Should().Contain("id=\"identifier-pointer-group\">");
    }

    [Fact]
    public async Task PostWorkbenchVerify_ValidOdcsYaml_Should_Return_200_With_Diagnostics()
    {
        // Arrange
        var odcsYaml = new OdcsContractYamlBuilder()
            .WithId("edc-ui-verify")
            .WithName("UiVerifyTest")
            .WithNestedSchoolUrn()
            .Build();

        var request = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["inputMode"] = "odcs",
            ["odcsYaml"] = odcsYaml,
            ["validFixtureJson"] = """{"school":{"urn":"123456"}}""",
            ["invalidFixtureJson"] = """{"school":{}}"""
        });

        // Act
        var result = await _harness.Get<PostWorkbenchVerifyEndpoint>().HandleAsync(request, CancellationToken.None);

        // Assert
        var html = result.Should().BeOfType<ContentResult>().Subject;
        html.StatusCode.Should().Be(StatusCodes.Status200OK);
        html.ContentType.Should().Be("text/html; charset=utf-8");
        html.Content.Should().Contain("<!DOCTYPE html>");
        html.Content.Should().Contain("Verification passed");
        html.Content.Should().Contain("id=\"validation-engine-diagnostics\"");
        html.Content.Should().Contain("json-schema");
    }

    [Fact]
    public async Task PostWorkbenchVerify_MalformedSchemaJson_Should_Return_422_With_ErrorSummary()
    {
        // Arrange
        var request = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["inputMode"] = "json",
            ["schemaJson"] = "{broken-json"
        });

        // Act
        var result = await _harness.Get<PostWorkbenchVerifyEndpoint>().HandleAsync(request, CancellationToken.None);

        // Assert
        var html = result.Should().BeOfType<ContentResult>().Subject;
        html.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        html.Content.Should().Contain("<!DOCTYPE html>");
        html.Content.Should().Contain("govuk-error-summary");
    }

    [Fact]
    public async Task PostWorkbenchVerify_MultiSeverityAndAnalyserFindings_Should_Return_422_With_Severity_Summary()
    {
        // Arrange
        const string analyserSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["urn", "quantity"],
          "properties": {
            "urn": { "type": "integer" },
            "quantity": { "type": "number", "maximum": 50 }
          }
        }
        """;
        const string analyserRules = """
        [
          {
            "id": "R-BAD-CASING",
            "description": "Wrong casing field",
            "engine": "cel",
            "path": "/",
            "severity": "ERROR",
            "implementation": "this.Quantity >= 0"
          },
          {
            "id": "R-WARN-LOOSE",
            "description": "Bound exceeds schema maximum",
            "engine": "cel",
            "path": "/",
            "severity": "WARNING",
            "implementation": "this.quantity >= 100"
          }
        ]
        """;

        var request = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["inputMode"] = "json",
            ["schemaJson"] = analyserSchema,
            ["rulesJson"] = analyserRules,
            ["validFixtureJson"] = """{"urn":100001,"quantity":10}"""
        });

        // Act
        var result = await _harness.Get<PostWorkbenchVerifyEndpoint>().HandleAsync(request, CancellationToken.None);

        // Assert
        var html = result.Should().BeOfType<ContentResult>().Subject;
        html.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        html.Content.Should().Contain("id=\"verify-issues-by-severity\"");
        html.Content.Should().Contain("id=\"verify-analyser-findings\"");
        html.Content.Should().Contain("R-BAD-CASING");
        html.Content.Should().Contain("R-WARN-LOOSE");
        html.Content.Should().Contain("Warning");
    }

    [Fact]
    public async Task PostWorkbenchVerify_SelectedLAEstabProperty_Should_AutoDerive_Type_And_Pass()
    {
        // Arrange
        var request = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["inputMode"] = "json",
            ["schemaJson"] = LaestabSchemaJson,
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "/establishment/laestab",
            ["validFixtureJson"] = """{"establishment":{"laestab":"201-3614"}}""",
            ["invalidFixtureJson"] = """{"establishment":{}}"""
        });

        // Act
        var result = await _harness.Get<PostWorkbenchVerifyEndpoint>().HandleAsync(request, CancellationToken.None);

        // Assert
        var html = result.Should().BeOfType<ContentResult>().Subject;
        html.StatusCode.Should().Be(StatusCodes.Status200OK);
        html.Content.Should().Contain("Verification passed");
        html.Content.Should().Contain("LAEstab");
        html.Content.Should().Contain("/establishment/laestab");
    }

    [Theory]
    [InlineData("", "identifierPointer")]
    [InlineData("/does/not/exist", "govuk-error-summary")]
    public async Task PostWorkbenchVerify_Empty_Or_Missing_Custom_Pointer_Should_Return_422(
        string customPointer,
        string expectedHtmlFragment)
    {
        // Arrange
        var request = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["inputMode"] = "json",
            ["schemaJson"] = LaestabSchemaJson,
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "__custom__",
            ["identifierPointer"] = customPointer
        });

        // Act
        var result = await _harness.Get<PostWorkbenchVerifyEndpoint>().HandleAsync(request, CancellationToken.None);

        // Assert
        var html = result.Should().BeOfType<ContentResult>().Subject;
        html.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        html.Content.Should().Contain("govuk-error-summary");
        html.Content.Should().Contain(expectedHtmlFragment);
    }

    [Fact]
    public async Task AdminUi_Submit_Inspect_And_Activate_Should_Publish_Schema_To_Public_Boundary()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("ui_journey");
        var submitReq = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["inputMode"] = "json",
            ["schemaName"] = schemaName,
            ["version"] = "1",
            ["schemaJson"] = JsonSchemaFixtures.ValidV1Schema,
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "/urn"
        });
        var dashboardHttp = new HttpRequestBuilder()
            .WithHeader("X-MS-CLIENT-PRINCIPAL-NAME", "admin.tester@education.gov.uk")
            .Build();
        var activateReq = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["lifecycleAction"] = "Activate",
            ["effectiveFromUtc"] = DateTimeOffset.UtcNow.AddMinutes(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm"),
            ["effectiveToUtc"] = DateTimeOffset.UtcNow.AddDays(14).UtcDateTime.ToString("yyyy-MM-ddTHH:mm"),
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "/urn"
        });

        // Act
        var submitRes = await _harness.Get<PostWorkbenchSubmitEndpoint>().HandleAsync(submitReq, CancellationToken.None);
        var dashboardRes = await _harness.Get<GetDashboardPage>().HandleAsync(dashboardHttp, CancellationToken.None);
        var detailRes = await _harness.Get<GetSchemaDetailPage>().HandleAsync(
            new HttpRequestBuilder().WithQuery("notice", "submitted").Build(), schemaName, 1, CancellationToken.None);
        var schedulePageRes = await _harness.Get<GetScheduleSchemaPage>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None);
        var scheduleSubmitRes = await _harness.Get<PostScheduleSchemaSubmitEndpoint>().HandleAsync(
            activateReq, schemaName, 1, CancellationToken.None);
        var publicSchema = await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, CancellationToken.None);

        // Assert
        submitRes.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"/manage/schemas/{schemaName}/1/details?notice=submitted");

        var dashboardHtml = dashboardRes.Should().BeOfType<ContentResult>().Subject;
        dashboardHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        dashboardHtml.Content.Should().Contain(schemaName);
        dashboardHtml.Content.Should().Contain("admin.tester@education.gov.uk");
        dashboardHtml.Content.Should().Contain("New version(s) ready to schedule");

        var detailHtml = detailRes.Should().BeOfType<ContentResult>().Subject;
        detailHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        detailHtml.Content.Should().Contain("passed verification and was saved as Validated");
        detailHtml.Content.Should().Contain("id=\"registered-validation-engines\"");

        var schedulePageHtml = schedulePageRes.Should().BeOfType<ContentResult>().Subject;
        schedulePageHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        schedulePageHtml.Content.Should().Contain($"Manage {schemaName}, V1");
        schedulePageHtml.Content.Should().Contain("/urn (integer, required)");
        schedulePageHtml.Content.Should().Contain("id=\"identifier-pointer-group\" hidden");

        scheduleSubmitRes.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"/manage/schemas/{schemaName}/1/details?notice=scheduled");

        var publicEntry = publicSchema.ShouldBeApiSuccess<SchemaCatalogueEntry>();
        publicEntry.SchemaName.Should().Be(schemaName);
        publicEntry.ActiveVersion.Should().Be(1);
    }
}
