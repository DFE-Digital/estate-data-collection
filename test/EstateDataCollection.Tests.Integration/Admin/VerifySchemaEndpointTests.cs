using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Schema.GetValidationEngines;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class VerifySchemaEndpointTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness<PostVerifySchemaEndpoint> _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetValidationEnginesEndpoint_Should_Expose_Registered_Engines()
    {
        // Arrange
        using var enginesHarness = new IntegrationEndpointHarness(fixture);
        var request = HttpRequestBuilder.Empty();

        // Act
        var result = await enginesHarness.Get<GetValidationEnginesEndpoint>()
            .HandleAsync(request, CancellationToken.None);

        // Assert
        var data = result.ShouldBeApiSuccess<GetValidationEnginesResult>(StatusCodes.Status200OK);
        data.Engines.Select(e => e.EngineId).Should().BeEquivalentTo(
        [
            "json-schema",
            "jsonlogic",
            "cel",
            "unique",
            "identifier-extractor",
            "cross-submission-enrichment",
            "reference-data-enrichment",
            "contract-consistency-analyzer"
        ]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("""{"schemaJson": "", "rulesJson": null, "odcsYaml": "  "}""")]
    public async Task When_Body_Is_Empty_Or_Has_No_Inputs_Should_Return_400_BadRequest(string rawBody)
    {
        // Arrange
        var request = HttpRequestBuilder.FromRaw(rawBody);

        // Act
        var result = await _harness.Endpoint.HandleAsync(request, CancellationToken.None);

        // Assert
        result.ShouldHaveStatusCode(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task When_Raw_Json_Array_Of_JsonLogic_And_Cel_Rules_Posted_Should_Return_200_OK()
    {
        // Arrange
        var rawRulesArray = new QualityRulesBuilder()
            .AddJsonLogicTimestampRule("RULE-JL-1")
            .AddCelRule("RULE-CEL-1", "school.capacity", "School capacity must be positive", "school.capacity > 0")
            .BuildJson();
        var request = HttpRequestBuilder.FromRaw(rawRulesArray);

        // Act
        var result = await _harness.Endpoint.HandleAsync(request, CancellationToken.None);

        // Assert
        result.ShouldHaveStatusCode(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_Unsupported_Engine_Posted_Should_Return_200_Diagnostic_Report_With_IsSuccessful_False()
    {
        // Arrange
        var unsupportedRules = new QualityRulesBuilder()
            .AddCustomRule("RULE-SQL-1", "dbt-sql", "school", "Unsupported SQL rule", "SELECT 1")
            .BuildJson();
        var request = HttpRequestBuilder.FromRaw(unsupportedRules);

        // Act
        var result = await _harness.Endpoint.HandleAsync(request, CancellationToken.None);

        // Assert
        var data = result.ShouldBeApiSuccess<VerifySchemaResult>(StatusCodes.Status200OK);
        data.IsSuccessful.Should().BeFalse();
        data.SyntaxErrors.Should().NotBeEmpty();
        data.Issues![RuleSeverity.Error].Should().NotBeEmpty();
    }

    [Fact]
    public async Task When_Raw_Odcs_Yaml_Posted_Without_Yaml_ContentType_Should_Fallback_Sniff_And_Succeed()
    {
        // Arrange
        var rawOdcsYaml = new OdcsContractYamlBuilder()
            .WithId("edc-sniff-test")
            .WithName("SniffTest")
            .Build();
        var http = new HttpRequestBuilder()
            .WithRawBody(rawOdcsYaml, "application/json")
            .Build();

        // Act
        var result = await _harness.Endpoint.HandleAsync(http, CancellationToken.None);

        // Assert
        var data = result.ShouldBeApiSuccess<VerifySchemaResult>(StatusCodes.Status200OK);
        data.IsSuccessful.Should().BeTrue();
        data.EngineTraces.Should().NotBeNull();
        data.EngineTraces!.Select(t => t.Descriptor.EngineId).Should().BeEquivalentTo(
            ["json-schema", "jsonlogic", "cel", "unique", "identifier-extractor", "contract-consistency-analyzer"]);
    }

    [Fact]
    public async Task When_Valid_Fixture_Violates_Schema_Should_Return_200_Diagnostic_Report_With_IsSuccessful_False()
    {
        // Arrange
        var dto = new VerifySchemaBodyDto(
            SchemaJson: JsonSchemaFixtures.ValidV1Schema,
            SampleValidFixtures: [new SurveyPayloadBuilder().WithoutUrn().BuildJson()],
            SampleInvalidFixtures: []);
        var request = HttpRequestBuilder.FromJson(dto);

        // Act
        var result = await _harness.Endpoint.HandleAsync(request, CancellationToken.None);

        // Assert
        var data = result.ShouldBeApiSuccess<VerifySchemaResult>(StatusCodes.Status200OK);
        data.IsSuccessful.Should().BeFalse();
        data.FixtureErrors.Should().NotBeEmpty();
        data.Issues![RuleSeverity.Error].Should().NotBeEmpty();
    }

    [Fact]
    public async Task When_Custom_LAEstab_IdentifierConfig_Is_Valid_Should_Return_200_With_Resolved_Required_True()
    {
        // Arrange
        const string schemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["establishment"],
          "properties": {
            "establishment": {
              "type": "object",
              "required": ["laestab"],
              "properties": {
                "laestab": { "type": "string", "minLength": 1 }
              }
            }
          }
        }
        """;

        var dto = new VerifySchemaBodyDto(
            SchemaJson: schemaJson,
            SampleValidFixtures: ["""{"establishment":{"laestab":"201-3614"}}"""],
            SampleInvalidFixtures: ["""{"establishment":{}}"""],
            IdentifierConfig: new SchemaIdentifierConfig
            {
                Type = "LAEstab",
                JsonPointer = "/establishment/laestab",
                Required = false
            });
        var request = HttpRequestBuilder.FromJson(dto);

        // Act
        var result = await _harness.Endpoint.HandleAsync(request, CancellationToken.None);

        // Assert
        var data = result.ShouldBeApiSuccess<VerifySchemaResult>(StatusCodes.Status200OK);
        data.ResolvedIdentifierConfig.Should().NotBeNull();
        data.ResolvedIdentifierConfig!.Type.Should().Be("LAEstab");
        data.ResolvedIdentifierConfig.JsonPointer.Should().Be("/establishment/laestab");
        data.ResolvedIdentifierConfig.Required.Should().BeTrue();
        data.EngineTraces.Should().NotBeNull().And.Contain(t => t.Descriptor.EngineId == "identifier-extractor");
    }

    [Fact]
    public async Task When_IdentifierPointer_Does_Not_Exist_In_Schema_Should_Return_200_Diagnostic_Report_With_IsSuccessful_False()
    {
        // Arrange
        var dto = new VerifySchemaBodyDto(
            SchemaJson: JsonSchemaFixtures.ValidV1Schema,
            IdentifierConfig: new SchemaIdentifierConfig
            {
                Type = "LAEstab",
                JsonPointer = "/nonexistent/laestab"
            });
        var request = HttpRequestBuilder.FromJson(dto);

        // Act
        var result = await _harness.Endpoint.HandleAsync(request, CancellationToken.None);

        // Assert
        var data = result.ShouldBeApiSuccess<VerifySchemaResult>(StatusCodes.Status200OK);
        data.IsSuccessful.Should().BeFalse();
        data.SyntaxErrors.Should().NotBeEmpty();
        data.Issues![RuleSeverity.Error].Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("/meta")]
    [InlineData("/is_active")]
    public async Task When_IdentifierPointer_Targets_NonScalar_Property_Should_Return_200_Diagnostic_Report_With_IsSuccessful_False(string pointer)
    {
        // Arrange
        const string schemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["meta", "is_active"],
          "properties": {
            "meta": {
              "type": "object",
              "properties": { "code": { "type": "string" } }
            },
            "is_active": { "type": "boolean" }
          }
        }
        """;

        var dto = new VerifySchemaBodyDto(
            SchemaJson: schemaJson,
            IdentifierConfig: new SchemaIdentifierConfig
            {
                Type = "CustomId",
                JsonPointer = pointer
            });
        var request = HttpRequestBuilder.FromJson(dto);

        // Act
        var result = await _harness.Endpoint.HandleAsync(request, CancellationToken.None);

        // Assert
        var data = result.ShouldBeApiSuccess<VerifySchemaResult>(StatusCodes.Status200OK);
        data.IsSuccessful.Should().BeFalse();
        data.SyntaxErrors.Should().NotBeEmpty();
        data.Issues![RuleSeverity.Error].Should().NotBeEmpty();
    }
}
