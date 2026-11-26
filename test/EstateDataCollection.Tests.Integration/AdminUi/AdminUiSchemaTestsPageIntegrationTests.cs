using System.Net;
using AwesomeAssertions;
using EstateDataCollection.AzureFunctions.Admin.Routes.Admin;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EstateDataCollection.Tests.Integration.AdminUi;

[Collection("AzuriteCollection")]
public sealed class AdminUiSchemaTestsPageIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    private const string CapacitySchemaJson = """
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

    private const string CapacityRulesJson = """
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

    [Fact]
    public async Task Workbench_Submit_With_Fixtures_Should_Display_Stored_Tests_On_Detail_And_Tests_Pages()
    {
        // Arrange
        var schemaName = await SubmitCapacitySchemaWithInitialFixturesAsync();

        // Act
        var detailRes = await _harness.Get<GetSchemaDetailPage>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None);
        var testsPageRes = await _harness.Get<GetSchemaTestsPage>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None);

        // Assert
        var detailHtml = detailRes.Should().BeOfType<ContentResult>().Subject;
        detailHtml.Content.Should().Contain("View Stored Tests (2)");
        detailHtml.Content.Should().Contain($"/manage/schemas/{schemaName}/1/tests/run");
        detailHtml.Content.Should().Contain("Active (1 rule)");

        var testsHtml = testsPageRes.Should().BeOfType<ContentResult>().Subject;
        testsHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        testsHtml.Content.Should().Contain("Valid Fixture #1");
        testsHtml.Content.Should().Contain("Invalid Fixture #1");
        testsHtml.Content.Should().Contain("positive-capacity");
        testsHtml.Content.Should().Contain("Not executed yet");
    }

    [Fact]
    public async Task RunSingle_And_RunAll_SchemaTests_Should_Execute_Rules_And_Persist_Diagnostics()
    {
        // Arrange
        var schemaName = await SubmitCapacitySchemaWithInitialFixturesAsync();

        // Act
        var runSingleRes = await _harness.Get<PostRunSchemaVersionTestsEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None);
        var detailAfterRunRes = await _harness.Get<GetSchemaDetailPage>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None);
        var runAllRes = await _harness.Get<PostRunAllSchemaTestsEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), CancellationToken.None);

        // Assert
        var runSingleHtml = runSingleRes.Should().BeOfType<ContentResult>().Subject;
        runSingleHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        runSingleHtml.Content.Should().Contain("Schema tests passed");
        runSingleHtml.Content.Should().Contain("Evaluated 1 valid fixture(s), 1 invalid fixture(s), and 1 business rule(s)");
        runSingleHtml.Content.Should().Contain("id=\"validation-engine-diagnostics\"");
        var decodedSingle = WebUtility.HtmlDecode(runSingleHtml.Content!);
        decodedSingle.Should().Contain("1 invalid fixture(s) failed validation");
        decodedSingle.Should().Contain("Failed — unknown if expected");
        decodedSingle.Should().Contain("[Rule:positive-capacity]");

        detailAfterRunRes.Should().BeOfType<ContentResult>()
            .Which.Content.Should().Contain("id=\"validation-engine-diagnostics\"");

        var runAllHtml = runAllRes.Should().BeOfType<ContentResult>().Subject;
        runAllHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        runAllHtml.Content.Should().Contain("id=\"schema-test-run-summary\"");
        runAllHtml.Content.Should().Contain($"{schemaName} v1");
        runAllHtml.Content.Should().Contain("Passed (warnings)");
        runAllHtml.Content.Should().Contain("jsonlogic: Succeeded");
    }

    [Fact]
    public async Task PostUpdateSchemaVersionTests_When_ValidFixture_Fails_Rule_Should_Return_422_Preserving_Input()
    {
        // Arrange
        var schemaName = await SubmitCapacitySchemaWithInitialFixturesAsync();
        var badUpdateReq = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["validFixtureJson"] = """{"urn":123456,"capacity":4}""",
            ["invalidFixtureJson"] = """{"urn":123456,"capacity":5}"""
        });

        // Act
        var badUpdateRes = await _harness.Get<PostUpdateSchemaVersionTestsEndpoint>().HandleAsync(
            badUpdateReq, schemaName, 1, CancellationToken.None);

        // Assert
        var badUpdateHtml = badUpdateRes.Should().BeOfType<ContentResult>().Subject;
        badUpdateHtml.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        badUpdateHtml.Content.Should().Contain("There is a problem");
        WebUtility.HtmlDecode(badUpdateHtml.Content!).Should().Contain("\"capacity\":4");
    }

    [Fact]
    public async Task PostUpdateSchemaVersionTests_When_ExpectedErrors_Mismatch_Should_Return_422_With_Comparison()
    {
        // Arrange
        var schemaName = await SubmitCapacitySchemaWithInitialFixturesAsync();
        var wrongExpectationReq = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["validFixtureJson"] = """[{"urn":123456,"capacity":50}]""",
            ["invalidFixtureJson"] = """[{"description":"Expect wrong rule","payload":{"urn":123456,"capacity":5},"expectedErrors":["NON-EXISTENT-RULE"]}]"""
        });

        // Act
        var wrongExpectationRes = await _harness.Get<PostUpdateSchemaVersionTestsEndpoint>().HandleAsync(
            wrongExpectationReq, schemaName, 1, CancellationToken.None);

        // Assert
        var html = wrongExpectationRes.Should().BeOfType<ContentResult>().Subject;
        html.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        html.Content.Should().Contain("NON-EXISTENT-RULE");
        html.Content.Should().Contain("positive-capacity");
    }

    [Fact]
    public async Task PostUpdateSchemaVersionTests_ValidMultiFixtures_Should_Save_Display_And_Rerun_All()
    {
        // Arrange
        var schemaName = await SubmitCapacitySchemaWithInitialFixturesAsync();
        var goodUpdateReq = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["validFixtureJson"] = """[{"urn":123456,"capacity":50},{"urn":654321,"capacity":75}]""",
            ["invalidFixtureJson"] = """[{"description":"Fails business rule","payload":{"urn":123456,"capacity":5},"expectedErrors":["positive-capacity"]},{"urn":123456,"capacity":8}]"""
        });

        // Act
        var goodUpdateRes = await _harness.Get<PostUpdateSchemaVersionTestsEndpoint>().HandleAsync(
            goodUpdateReq, schemaName, 1, CancellationToken.None);
        var afterUpdatePageRes = await _harness.Get<GetSchemaTestsPage>().HandleAsync(
            new HttpRequestBuilder().WithQuery("notice", "updated").Build(),
            schemaName,
            1,
            CancellationToken.None);
        var rerunRes = await _harness.Get<PostRunSchemaVersionTestsEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None);

        // Assert
        goodUpdateRes.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be($"/manage/schemas/{Uri.EscapeDataString(schemaName)}/1/tests?notice=updated");

        var afterUpdateHtml = afterUpdatePageRes.Should().BeOfType<ContentResult>().Subject;
        afterUpdateHtml.StatusCode.Should().Be(StatusCodes.Status200OK);
        var decoded = WebUtility.HtmlDecode(afterUpdateHtml.Content!);
        decoded.Should().Contain($"Stored regression test payloads verified and saved for '{schemaName}' v1.");
        decoded.Should().Contain("Valid Fixture #2");
        decoded.Should().Contain("Invalid Fixture #2");
        decoded.Should().Contain("Fails business rule");
        decoded.Should().Contain("Matched expected error(s)");
        decoded.Should().Contain("Failed — unknown if expected");
        decoded.Should().Contain("[Rule:positive-capacity]");
        decoded.Should().Contain("\"capacity\": 75");
        decoded.Should().Contain("\"capacity\": 8");

        var rerunHtml = rerunRes.Should().BeOfType<ContentResult>().Subject;
        rerunHtml.Content.Should().Contain("Evaluated 2 valid fixture(s), 2 invalid fixture(s), and 1 business rule(s)");
        rerunHtml.Content.Should().Contain("Triggered errors (1)");
    }

    private async Task<string> SubmitCapacitySchemaWithInitialFixturesAsync()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("ui_tests");
        var submitReq = await HttpRequestBuilder.FromFormAsync(new Dictionary<string, string?>
        {
            ["inputMode"] = "json",
            ["schemaName"] = schemaName,
            ["version"] = "1",
            ["schemaJson"] = CapacitySchemaJson,
            ["rulesJson"] = CapacityRulesJson,
            ["validFixtureJson"] = """{"urn":123456,"capacity":50}""",
            ["invalidFixtureJson"] = """{"urn":123456,"capacity":5}""",
            ["identifierMode"] = "enabled",
            ["identifierProperty"] = "/urn"
        });

        var submitRes = await _harness.Get<PostWorkbenchSubmitEndpoint>().HandleAsync(submitReq, CancellationToken.None);
        submitRes.Should().BeOfType<RedirectResult>();
        return schemaName;
    }
}
