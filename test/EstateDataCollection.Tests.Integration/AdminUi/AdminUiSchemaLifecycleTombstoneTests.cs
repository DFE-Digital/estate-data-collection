using System.Net;
using AwesomeAssertions;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Admin;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EstateDataCollection.Tests.Integration.AdminUi;

[Collection("AzuriteCollection")]
public sealed class AdminUiSchemaLifecycleTombstoneTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    private const string SchemaJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["urn", "quantity"],
      "properties": {
        "urn": { "type": "integer", "minimum": 1 },
        "quantity": { "type": "number", "minimum": 0, "maximum": 100 }
      }
    }
    """;

    private const string ValidFixture = """{"urn": 100001, "quantity": 60}""";

    private const string BrokenRulesJson = """
    [
      {
        "id": "R-MISSING-FIELD",
        "description": "References missing field",
        "engine": "cel",
        "path": "/",
        "severity": "ERROR",
        "implementation": "this.nonExistentField > 0"
      }
    ]
    """;

    private const string WarningRulesJson = """
    [
      {
        "id": "R-WARN-FLOOR",
        "description": "Advisory quantity check",
        "engine": "cel",
        "path": "/",
        "severity": "WARNING",
        "implementation": "this.quantity >= 80"
      }
    ]
    """;

    [Fact]
    public async Task Invalid_Schema_Should_Render_ErrorReport_On_Dashboard_And_Detail_And_Hide_Schedule()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("ui_err");
        await SubmitSchemaVersionAsync(schemaName, 1, BrokenRulesJson);

        // Act
        var dashRes = await _harness.Get<GetDashboardPage>().HandleAsync(HttpRequestBuilder.Empty(), CancellationToken.None);
        var detailRes = await _harness.Get<GetSchemaDetailPage>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None);

        // Assert
        var dashHtml = WebUtility.HtmlDecode(dashRes.Should().BeOfType<ContentResult>().Subject.Content!);
        dashHtml.Should().Contain(schemaName);
        dashHtml.Should().Contain(".error.json");
        dashHtml.Should().Contain("Schema diagnostics require review");

        var detailHtml = WebUtility.HtmlDecode(detailRes.Should().BeOfType<ContentResult>().Subject.Content!);
        detailHtml.Should().Contain("id=\"schema-error-report-card\"");
        detailHtml.Should().Contain("Invalid (.error.json)");
        detailHtml.Should().Contain("R-MISSING-FIELD");
        detailHtml.Should().Contain("Validation policy");
        detailHtml.Should().NotContain("Manage Schedule & Lifecycle");
    }

    [Fact]
    public async Task Delete_Schema_Via_AdminUi_Should_Render_Tombstone_Instructing_Next_Version()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("ui_del");
        await SubmitSchemaVersionAsync(schemaName, 1, BrokenRulesJson);

        // Act
        var deleteRes = await _harness.Get<PostSchemaDeleteEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None);
        var detailRes = await _harness.Get<GetSchemaDetailPage>().HandleAsync(
            new HttpRequestBuilder().WithQuery("notice", "deleted").Build(),
            schemaName,
            1,
            CancellationToken.None);

        // Assert
        deleteRes.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"/manage/schemas/{schemaName}/1/details?notice=deleted");

        var deletedHtml = WebUtility.HtmlDecode(detailRes.Should().BeOfType<ContentResult>().Subject.Content!);
        deletedHtml.Should().Contain("id=\"schema-deleted-tombstone\"");
        deletedHtml.Should().Contain("push any fix to <code class=\"dfe-code-badge\">v2</code>");
        deletedHtml.Should().Contain("id=\"schema-error-report-card\"");
        deletedHtml.Should().Contain("Errors (.error.json)");
        deletedHtml.Should().NotContain("id=\"delete-schema-button\"");
        deletedHtml.Should().NotContain("View Stored Tests");
    }

    [Fact]
    public async Task Unvalidated_Schema_With_Warnings_Should_Show_SignOff_Panel_And_Unlock_Scheduling_After_SignOff()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("ui_signoff");
        await SubmitSchemaVersionAsync(schemaName, 2, WarningRulesJson);

        var unvalidatedDetailRes = await _harness.Get<GetSchemaDetailPage>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 2, CancellationToken.None);

        var signOffReq = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["signedOffBy"] = "qa.reviewer@education.gov.uk"
        });

        // Act
        var signOffRes = await _harness.Get<PostSchemaSignOffEndpoint>().HandleAsync(
            signOffReq, schemaName, 2, CancellationToken.None);
        var signedOffDetailRes = await _harness.Get<GetSchemaDetailPage>().HandleAsync(
            new HttpRequestBuilder().WithQuery("notice", "signedoff").Build(),
            schemaName,
            2,
            CancellationToken.None);

        // Assert
        var unvalidatedHtml = WebUtility.HtmlDecode(unvalidatedDetailRes.Should().BeOfType<ContentResult>().Subject.Content!);
        unvalidatedHtml.Should().Contain("Unvalidated");
        unvalidatedHtml.Should().Contain("id=\"schema-signoff-panel\"");
        unvalidatedHtml.Should().Contain("Warnings (.error.json)");
        unvalidatedHtml.Should().Contain("R-WARN-FLOOR");
        unvalidatedHtml.Should().NotContain("Manage Schedule & Lifecycle");

        signOffRes.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"/manage/schemas/{schemaName}/2/details?notice=signedoff");

        var signedOffHtml = WebUtility.HtmlDecode(signedOffDetailRes.Should().BeOfType<ContentResult>().Subject.Content!);
        signedOffHtml.Should().Contain("qa.reviewer@education.gov.uk");
        signedOffHtml.Should().Contain("Validated");
        signedOffHtml.Should().Contain("Manage Schedule & Lifecycle");
    }

    private async Task SubmitSchemaVersionAsync(string schemaName, int version, string rulesJson)
    {
        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: SchemaJson,
                    RulesJson: rulesJson,
                    SampleValidFixtures: [ValidFixture])),
                schemaName,
                version,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);
    }
}
