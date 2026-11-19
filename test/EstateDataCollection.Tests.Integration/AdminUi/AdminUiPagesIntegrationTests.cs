using System.Net;
using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Rendering;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.Admin.Routes.Admin;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EstateDataCollection.Tests.Integration.AdminUi;

[Collection("AzuriteCollection")]
public sealed class AdminUiPagesIntegrationTests
{
    private readonly IntegrationEndpointHarness _harness;
    private readonly GetStaticAssetEndpoint _staticAssetEndpoint;
    private readonly GetDashboardPage _dashboardPageEndpoint;
    private readonly GetSchemaDetailPage _schemaDetailPageEndpoint;
    private readonly GetSchemaTestsPage _schemaTestsPageEndpoint;
    private readonly PostUpdateSchemaVersionTestsEndpoint _updateSchemaVersionTestsEndpoint;
    private readonly PostRunSchemaVersionTestsEndpoint _runSchemaVersionTestsEndpoint;
    private readonly PostRunAllSchemaTestsEndpoint _runAllSchemaTestsEndpoint;
    private readonly GetScheduleSchemaPage _scheduleSchemaPageEndpoint;
    private readonly PostScheduleSchemaSubmitEndpoint _scheduleSchemaSubmitEndpoint;
    private readonly GetWorkbenchPage _workbenchPageEndpoint;
    private readonly PostWorkbenchVerifyEndpoint _workbenchVerifyEndpoint;
    private readonly PostWorkbenchSubmitEndpoint _workbenchSubmitEndpoint;

    public AdminUiPagesIntegrationTests(AzuriteFixture fixture)
    {
        _harness = new IntegrationEndpointHarness(fixture);

        var renderer = new RazorComponentRenderer(
            new ServiceCollection().BuildServiceProvider(),
            NullLoggerFactory.Instance);
        var getSchemasUseCase = new GetSchemasUseCase(_harness.SchemaRegistry);
        var getSchemaDetailUseCase = new GetSchemaDetailUseCase(_harness.SchemaRegistry);
        var verifyUseCase = new VerifySchemaUseCase(_harness.SchemaRegistry, odcsParser: new Infrastructure.Registry.OdcsContractParser());
        var submitUseCase = new SubmitSchemaUseCase(_harness.SchemaRegistry, odcsParser: new Infrastructure.Registry.OdcsContractParser());
        var scheduleUseCase = new ScheduleSchemaUseCase(_harness.SchemaRegistry);
        var runTestsUseCase = new RunSchemaTestsUseCase(_harness.SchemaRegistry);
        var updateTestsUseCase = new UpdateSchemaTestsUseCase(_harness.SchemaRegistry);

        _staticAssetEndpoint = new GetStaticAssetEndpoint();
        _dashboardPageEndpoint = new GetDashboardPage(getSchemasUseCase, renderer);
        _schemaDetailPageEndpoint = new GetSchemaDetailPage(getSchemaDetailUseCase, renderer);
        _schemaTestsPageEndpoint = new GetSchemaTestsPage(getSchemaDetailUseCase, renderer);
        _updateSchemaVersionTestsEndpoint = new PostUpdateSchemaVersionTestsEndpoint(updateTestsUseCase, getSchemaDetailUseCase, renderer);
        _runSchemaVersionTestsEndpoint = new PostRunSchemaVersionTestsEndpoint(runTestsUseCase, getSchemaDetailUseCase, renderer);
        _runAllSchemaTestsEndpoint = new PostRunAllSchemaTestsEndpoint(runTestsUseCase, getSchemasUseCase, renderer);
        _scheduleSchemaPageEndpoint = new GetScheduleSchemaPage(getSchemaDetailUseCase, renderer);
        _scheduleSchemaSubmitEndpoint = new PostScheduleSchemaSubmitEndpoint(scheduleUseCase, getSchemaDetailUseCase, renderer);
        _workbenchPageEndpoint = new GetWorkbenchPage(renderer);
        _workbenchVerifyEndpoint = new PostWorkbenchVerifyEndpoint(verifyUseCase, renderer);
        _workbenchSubmitEndpoint = new PostWorkbenchSubmitEndpoint(submitUseCase, renderer);
    }

    private static async Task<HttpRequest> CreateFormRequestAsync(
        Dictionary<string, string?> fields,
        Dictionary<string, string>? headers = null)
    {
        var context = new DefaultHttpContext();
        var content = new FormUrlEncodedContent(fields.Select(kv => new KeyValuePair<string?, string?>(kv.Key, kv.Value)));
        var bytes = await content.ReadAsByteArrayAsync();
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/x-www-form-urlencoded";

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                context.Request.Headers[key] = value;
            }
        }

        return context.Request;
    }

    [Fact]
    public void StaticAssetEndpoint_Should_Serve_Embedded_Assets_And_Return_404_For_Missing()
    {
        var cssResult = _staticAssetEndpoint.Handle(HttpRequestBuilder.Empty(), "css/dfe-branding.css");
        var cssFile = cssResult.Should().BeOfType<FileContentResult>().Subject;
        cssFile.ContentType.Should().Be("text/css; charset=utf-8");
        cssFile.FileContents.Length.Should().BeGreaterThan(0);

        var imgResult = _staticAssetEndpoint.Handle(HttpRequestBuilder.Empty(), "images/govuk-crest.svg");
        var imgFile = imgResult.Should().BeOfType<FileContentResult>().Subject;
        imgFile.ContentType.Should().Be("image/svg+xml");

        var missing = _staticAssetEndpoint.Handle(HttpRequestBuilder.Empty(), "css/does-not-exist.css");
        missing.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task WorkbenchVerifyEndpoint_Should_Verify_Odcs_Yaml_For_Both_Fetch_Partial_And_Full_Page()
    {
        var odcsYaml = new OdcsContractYamlBuilder()
            .WithId("edc-ui-verify")
            .WithName("UiVerifyTest")
            .WithNestedSchoolUrn()
            .Build();

        var fields = new Dictionary<string, string?>
        {
            ["inputMode"] = "odcs",
            ["odcsYaml"] = odcsYaml,
            ["validFixtureJson"] = """{"school":{"urn":"123456"}}""",
            ["invalidFixtureJson"] = """{"school":{}}"""
        };

        var fetchReq = await CreateFormRequestAsync(
            fields,
            headers: new Dictionary<string, string>
            {
                ["X-Requested-With"] = "fetch"
            });

        var partialResult = await _workbenchVerifyEndpoint.HandleAsync(fetchReq, CancellationToken.None);
        var partialHtml = partialResult.Should().BeOfType<ContentResult>().Subject;
        partialHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        partialHtml.ContentType.Should().Be("text/html; charset=utf-8");
        partialHtml.Content.Should().Contain("Verification passed");
        partialHtml.Content.Should().NotContain("<!DOCTYPE html>");

        var fullPageReq = await CreateFormRequestAsync(fields);
        var fullPageResult = await _workbenchVerifyEndpoint.HandleAsync(fullPageReq, CancellationToken.None);
        var fullPageHtml = fullPageResult.Should().BeOfType<ContentResult>().Subject;
        fullPageHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        fullPageHtml.Content.Should().Contain("<!DOCTYPE html>");
        fullPageHtml.Content.Should().Contain("Verification passed");

        var badFetchReq = await CreateFormRequestAsync(
            new Dictionary<string, string?>
            {
                ["inputMode"] = "json",
                ["schemaJson"] = "{broken-json"
            },
            headers: new Dictionary<string, string>
            {
                ["X-Requested-With"] = "fetch"
            });

        var errorPartial = await _workbenchVerifyEndpoint.HandleAsync(badFetchReq, CancellationToken.None);
        var errorContent = errorPartial.Should().BeOfType<ContentResult>().Subject;
        errorContent.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        errorContent.Content.Should().Contain("govuk-error-summary");
    }

    [Fact]
    public async Task WorkbenchVerifyEndpoint_Should_AutoDerive_LAEstab_From_Selected_Property_And_Reject_Missing_Or_Empty_Custom_Pointer()
    {
        const string laestabSchema = """
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

        var okReq = await CreateFormRequestAsync(
            new Dictionary<string, string?>
            {
                ["inputMode"] = "json",
                ["schemaJson"] = laestabSchema,
                ["identifierMode"] = "enabled",
                ["identifierProperty"] = "/establishment/laestab",
                ["validFixtureJson"] = """{"establishment":{"laestab":"201-3614"}}""",
                ["invalidFixtureJson"] = """{"establishment":{}}"""
            },
            headers: new Dictionary<string, string> { ["X-Requested-With"] = "fetch" });

        var okRes = await _workbenchVerifyEndpoint.HandleAsync(okReq, CancellationToken.None);
        var okHtml = okRes.Should().BeOfType<ContentResult>().Subject;
        okHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        okHtml.Content.Should().Contain("Verification passed");
        okHtml.Content.Should().Contain("LAEstab");
        okHtml.Content.Should().Contain("/establishment/laestab");

        var emptyPointerReq = await CreateFormRequestAsync(
            new Dictionary<string, string?>
            {
                ["inputMode"] = "json",
                ["schemaJson"] = laestabSchema,
                ["identifierMode"] = "enabled",
                ["identifierProperty"] = "__custom__",
                ["identifierPointer"] = ""
            },
            headers: new Dictionary<string, string> { ["X-Requested-With"] = "fetch" });

        var emptyRes = await _workbenchVerifyEndpoint.HandleAsync(emptyPointerReq, CancellationToken.None);
        var emptyHtml = emptyRes.Should().BeOfType<ContentResult>().Subject;
        emptyHtml.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        emptyHtml.Content.Should().Contain("govuk-error-summary");
        emptyHtml.Content.Should().Contain("identifierPointer");

        var badPointerReq = await CreateFormRequestAsync(
            new Dictionary<string, string?>
            {
                ["inputMode"] = "json",
                ["schemaJson"] = laestabSchema,
                ["identifierMode"] = "enabled",
                ["identifierProperty"] = "__custom__",
                ["identifierPointer"] = "/does/not/exist"
            },
            headers: new Dictionary<string, string> { ["X-Requested-With"] = "fetch" });

        var badRes = await _workbenchVerifyEndpoint.HandleAsync(badPointerReq, CancellationToken.None);
        var badHtml = badRes.Should().BeOfType<ContentResult>().Subject;
        badHtml.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        badHtml.Content.Should().Contain("govuk-error-summary");
    }

    [Fact]
    public async Task AdminUi_Full_Journey_Submit_Inspect_Dashboard_And_Activate_Should_Publish_To_Public_Boundary()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("ui_journey");

        var getWorkbench = await _workbenchPageEndpoint.HandleAsync(HttpRequestBuilder.Empty());
        var workbenchContent = getWorkbench.Should().BeOfType<ContentResult>().Subject;
        workbenchContent.StatusCode.Should().Be(StatusCodes.Status200OK);
        workbenchContent.Content.Should().Contain("Contract Workbench");
        workbenchContent.Content.Should().NotContain("Platform default");
        WebUtility.HtmlDecode(workbenchContent.Content).Should().Contain("Not set — enter or select a property");
        workbenchContent.Content.Should().Contain("id=\"identifier-pointer-group\">");

        var submitReq = await CreateFormRequestAsync(new Dictionary<string, string?>
        {
            ["inputMode"] = "json",
            ["schemaName"] = schemaName,
            ["version"] = "1",
            ["schemaJson"] = JsonSchemaFixtures.ValidV1Schema,
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "/urn"
        });

        var submitRes = await _workbenchSubmitEndpoint.HandleAsync(submitReq, CancellationToken.None);
        var submitRedirect = submitRes.Should().BeOfType<RedirectResult>().Subject;
        submitRedirect.Url.Should().Be($"/manage/schemas/{schemaName}/1/details?notice=submitted");

        var dashboardHttp = new HttpRequestBuilder()
            .WithHeader("X-MS-CLIENT-PRINCIPAL-NAME", "admin.tester@education.gov.uk")
            .Build();
        var dashboardRes = await _dashboardPageEndpoint.HandleAsync(dashboardHttp, CancellationToken.None);
        var dashboardHtml = dashboardRes.Should().BeOfType<ContentResult>().Subject;
        dashboardHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        dashboardHtml.Content.Should().Contain(schemaName);
        dashboardHtml.Content.Should().Contain("admin.tester@education.gov.uk");
        dashboardHtml.Content.Should().Contain("New version(s) ready to schedule");

        var detailHttp = new HttpRequestBuilder().WithQuery("notice", "submitted").Build();
        var detailRes = await _schemaDetailPageEndpoint.HandleAsync(detailHttp, schemaName, 1, CancellationToken.None);
        var detailHtml = detailRes.Should().BeOfType<ContentResult>().Subject;
        detailHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        detailHtml.Content.Should().Contain("passed verification and was saved as Validated");
        detailHtml.Content.Should().Contain(schemaName);

        var schedulePageRes = await _scheduleSchemaPageEndpoint.HandleAsync(
            HttpRequestBuilder.Empty(),
            schemaName,
            1,
            CancellationToken.None);
        var schedulePageHtml = schedulePageRes.Should().BeOfType<ContentResult>().Subject;
        schedulePageHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        schedulePageHtml.Content.Should().Contain($"Manage {schemaName}, V1");
        schedulePageHtml.Content.Should().NotContain("Platform default");
        schedulePageHtml.Content.Should().Contain("/urn (integer, required)");
        schedulePageHtml.Content.Should().Contain("id=\"identifier-pointer-group\" hidden");

        var activateReq = await CreateFormRequestAsync(new Dictionary<string, string?>
        {
            ["lifecycleAction"] = "Activate",
            ["effectiveFromUtc"] = DateTimeOffset.UtcNow.AddMinutes(-5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm"),
            ["effectiveToUtc"] = DateTimeOffset.UtcNow.AddDays(14).UtcDateTime.ToString("yyyy-MM-ddTHH:mm"),
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "/urn"
        });

        var scheduleSubmitRes = await _scheduleSchemaSubmitEndpoint.HandleAsync(
            activateReq,
            schemaName,
            1,
            CancellationToken.None);
        var scheduleRedirect = scheduleSubmitRes.Should().BeOfType<RedirectResult>().Subject;
        scheduleRedirect.Url.Should().Be($"/manage/schemas/{schemaName}/1/details?notice=scheduled");

        // Verify the Public boundary now exposes the schema cleanly
        var publicSchema = await _harness.PublicGetSchemaByName.HandleAsync(
            HttpRequestBuilder.Empty(),
            schemaName,
            CancellationToken.None);
        var publicEntry = publicSchema.ShouldBeApiSuccess<SchemaCatalogueEntry>();
        publicEntry.SchemaName.Should().Be(schemaName);
        publicEntry.ActiveVersion.Should().Be(1);
    }

    [Fact]
    public async Task SchemaTestsPage_And_RunTests_Endpoints_Should_Persist_Display_And_Execute_BusinessLogic_Fixtures()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("ui_tests");

        const string schemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["urn", "capacity"],
          "properties": {
            "urn": { "type": "integer", "minimum": 1 },
            "capacity": { "type": "integer" }
          }
        }
        """;

        const string rulesJson = """
        [
          {
            "id": "positive-capacity",
            "description": "Capacity must be at least 10",
            "severity": "ERROR",
            "path": "/",
            "message": "Capacity must be >= 10",
            "assert": { ">=": [ { "var": "capacity" }, 10 ] }
          }
        ]
        """;

        // Invalid fixture satisfies JSON Schema (capacity is an integer = 5) but FAILS business logic (capacity >= 10).
        var submitReq = await CreateFormRequestAsync(new Dictionary<string, string?>
        {
            ["inputMode"] = "json",
            ["schemaName"] = schemaName,
            ["version"] = "1",
            ["schemaJson"] = schemaJson,
            ["rulesJson"] = rulesJson,
            ["validFixtureJson"] = """{"urn":123456,"capacity":50}""",
            ["invalidFixtureJson"] = """{"urn":123456,"capacity":5}""",
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "/urn"
        });

        var submitRes = await _workbenchSubmitEndpoint.HandleAsync(submitReq, CancellationToken.None);
        submitRes.Should().BeOfType<RedirectResult>();

        var detailRes = await _schemaDetailPageEndpoint.HandleAsync(
            HttpRequestBuilder.Empty(),
            schemaName,
            1,
            CancellationToken.None);
        var detailHtml = detailRes.Should().BeOfType<ContentResult>().Subject;
        detailHtml.Content.Should().Contain("View Stored Tests (2)");
        detailHtml.Content.Should().Contain($"/manage/schemas/{schemaName}/1/tests/run");

        var testsPageRes = await _schemaTestsPageEndpoint.HandleAsync(
            HttpRequestBuilder.Empty(),
            schemaName,
            1,
            CancellationToken.None);
        var testsPageHtml = testsPageRes.Should().BeOfType<ContentResult>().Subject;
        testsPageHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        testsPageHtml.Content.Should().Contain("Valid Fixture #1");
        testsPageHtml.Content.Should().Contain("Invalid Fixture #1");
        testsPageHtml.Content.Should().Contain("positive-capacity");
        testsPageHtml.Content.Should().Contain("Not executed yet");

        var runSingleRes = await _runSchemaVersionTestsEndpoint.HandleAsync(
            HttpRequestBuilder.Empty(),
            schemaName,
            1,
            CancellationToken.None);
        var runSingleHtml = runSingleRes.Should().BeOfType<ContentResult>().Subject;
        runSingleHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        runSingleHtml.Content.Should().Contain("Schema tests passed");
        runSingleHtml.Content.Should().Contain("Evaluated 1 valid fixture(s), 1 invalid fixture(s), and 1 business rule(s)");
        var decodedSingle = WebUtility.HtmlDecode(runSingleHtml.Content!);
        decodedSingle.Should().Contain("Soft warning: 1 invalid fixture(s) failed validation");
        decodedSingle.Should().Contain("Failed — unknown if expected");
        decodedSingle.Should().Contain("[Rule:positive-capacity]");

        var runAllRes = await _runAllSchemaTestsEndpoint.HandleAsync(
            HttpRequestBuilder.Empty(),
            CancellationToken.None);
        var runAllHtml = runAllRes.Should().BeOfType<ContentResult>().Subject;
        runAllHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        runAllHtml.Content.Should().Contain("id=\"schema-test-run-summary\"");
        runAllHtml.Content.Should().Contain($"{schemaName} v1");
        runAllHtml.Content.Should().Contain("Passed (warnings)");

        // Attempt invalid update (valid fixture fails business rule capacity >= 10) -> 422 preserving user input and not overwriting stored fixtures
        var badUpdateReq = await CreateFormRequestAsync(new Dictionary<string, string?>
        {
            ["validFixtureJson"] = """{"urn":123456,"capacity":4}""",
            ["invalidFixtureJson"] = """{"urn":123456,"capacity":5}"""
        });
        var badUpdateRes = await _updateSchemaVersionTestsEndpoint.HandleAsync(badUpdateReq, schemaName, 1, CancellationToken.None);
        var badUpdateHtml = badUpdateRes.Should().BeOfType<ContentResult>().Subject;
        badUpdateHtml.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        badUpdateHtml.Content.Should().Contain("There is a problem");
        WebUtility.HtmlDecode(badUpdateHtml.Content!).Should().Contain("\"capacity\":4");

        // Attempt update where invalid fixture passes wrong expectedErrors assertion -> 422 showing both expected and actual errors
        var wrongExpectationReq = await CreateFormRequestAsync(new Dictionary<string, string?>
        {
            ["validFixtureJson"] = """[{"urn":123456,"capacity":50}]""",
            ["invalidFixtureJson"] = """[{"description":"Expect wrong rule","payload":{"urn":123456,"capacity":5},"expectedErrors":["NON-EXISTENT-RULE"]}]"""
        });
        var wrongExpectationRes = await _updateSchemaVersionTestsEndpoint.HandleAsync(wrongExpectationReq, schemaName, 1, CancellationToken.None);
        var wrongExpectationHtml = wrongExpectationRes.Should().BeOfType<ContentResult>().Subject;
        wrongExpectationHtml.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        wrongExpectationHtml.Content.Should().Contain("NON-EXISTENT-RULE");
        wrongExpectationHtml.Content.Should().Contain("positive-capacity");

        // Valid update adding multiple valid and invalid fixtures (one with expectedErrors, one without) -> 302 redirect to ?notice=updated
        var goodUpdateReq = await CreateFormRequestAsync(new Dictionary<string, string?>
        {
            ["validFixtureJson"] = """[{"urn":123456,"capacity":50},{"urn":654321,"capacity":75}]""",
            ["invalidFixtureJson"] = """[{"description":"Fails business rule","payload":{"urn":123456,"capacity":5},"expectedErrors":["positive-capacity"]},{"urn":123456,"capacity":8}]"""
        });
        var goodUpdateRes = await _updateSchemaVersionTestsEndpoint.HandleAsync(goodUpdateReq, schemaName, 1, CancellationToken.None);
        var redirect = goodUpdateRes.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be($"/manage/schemas/{Uri.EscapeDataString(schemaName)}/1/tests?notice=updated");

        // Load SchemaTestsPage with ?notice=updated and confirm banner, matched assertion, soft warning on raw #2, & all 4 updated fixtures render
        var afterUpdatePageRes = await _schemaTestsPageEndpoint.HandleAsync(
            new HttpRequestBuilder().WithQuery("notice", "updated").Build(),
            schemaName,
            1,
            CancellationToken.None);
        var afterUpdateHtml = afterUpdatePageRes.Should().BeOfType<ContentResult>().Subject;
        afterUpdateHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        var decodedAfterUpdate = WebUtility.HtmlDecode(afterUpdateHtml.Content!);
        decodedAfterUpdate.Should().Contain($"Stored regression test payloads verified and saved for '{schemaName}' v1.");
        decodedAfterUpdate.Should().Contain("Valid Fixture #2");
        decodedAfterUpdate.Should().Contain("Invalid Fixture #2");
        decodedAfterUpdate.Should().Contain("Fails business rule");
        decodedAfterUpdate.Should().Contain("Matched expected error(s)");
        decodedAfterUpdate.Should().Contain("Failed — unknown if expected");
        decodedAfterUpdate.Should().Contain("[Rule:positive-capacity]");
        decodedAfterUpdate.Should().Contain("\"capacity\": 75");
        decodedAfterUpdate.Should().Contain("\"capacity\": 8");

        // Re-run stored tests after update -> evaluates all 2 valid + 2 invalid + 1 business rule
        var rerunRes = await _runSchemaVersionTestsEndpoint.HandleAsync(HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None);
        var rerunHtml = rerunRes.Should().BeOfType<ContentResult>().Subject;
        rerunHtml.Content.Should().Contain("Evaluated 2 valid fixture(s), 2 invalid fixture(s), and 1 business rule(s)");
        rerunHtml.Content.Should().Contain("Triggered errors (1)");
    }
}
