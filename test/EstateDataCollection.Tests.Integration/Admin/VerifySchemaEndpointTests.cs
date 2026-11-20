using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
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
{
    private readonly IntegrationEndpointHarness<PostVerifySchemaEndpoint> _harness = new(fixture);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("""{"schemaJson": "", "rulesJson": null, "odcsYaml": "  "}""")]
    public async Task When_Body_Is_Empty_Or_Has_No_Inputs_Should_Return_400_BadRequest(string rawBody)
    {
        var result = await _harness.Endpoint.HandleAsync(
            HttpRequestBuilder.FromRaw(rawBody),
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task When_Raw_Odcs_Yaml_Posted_Without_Yaml_ContentType_Should_Fallback_Sniff_And_Succeed()
    {
        var rawOdcsYaml = new OdcsContractYamlBuilder()
            .WithId("edc-sniff-test")
            .WithName("SniffTest")
            .Build();

        var http = new HttpRequestBuilder()
            .WithRawBody(rawOdcsYaml, "application/json")
            .Build();

        var result = await _harness.Endpoint.HandleAsync(http, CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_Valid_Fixture_Violates_Schema_Should_Return_422_UnprocessableEntity()
    {
        var dto = new VerifySchemaBodyDto(
            SchemaJson: JsonSchemaFixtures.ValidV1Schema,
            SampleValidFixtures: [new SurveyPayloadBuilder().WithoutUrn().BuildJson()],
            SampleInvalidFixtures: []);

        var result = await _harness.Endpoint.HandleAsync(
            HttpRequestBuilder.FromJson(dto),
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_Custom_LAEstab_IdentifierConfig_Is_Valid_Should_Return_200_With_Resolved_Required_True()
    {
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

        var result = await _harness.Endpoint.HandleAsync(
            HttpRequestBuilder.FromJson(dto),
            CancellationToken.None);

        var data = result.ShouldBeApiSuccess<VerifySchemaResult>(StatusCodes.Status200OK);
        data.ResolvedIdentifierConfig.Should().NotBeNull();
        data.ResolvedIdentifierConfig!.Type.Should().Be("LAEstab");
        data.ResolvedIdentifierConfig.JsonPointer.Should().Be("/establishment/laestab");
        data.ResolvedIdentifierConfig.Required.Should().BeTrue();
    }

    [Fact]
    public async Task When_IdentifierPointer_Does_Not_Exist_In_Schema_Should_Return_422_UnprocessableEntity()
    {
        var dto = new VerifySchemaBodyDto(
            SchemaJson: JsonSchemaFixtures.ValidV1Schema,
            IdentifierConfig: new SchemaIdentifierConfig
            {
                Type = "LAEstab",
                JsonPointer = "/nonexistent/laestab"
            });

        var result = await _harness.Endpoint.HandleAsync(
            HttpRequestBuilder.FromJson(dto),
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Theory]
    [InlineData("/meta")]
    [InlineData("/is_active")]
    public async Task When_IdentifierPointer_Targets_NonScalar_Property_Should_Return_422_UnprocessableEntity(string pointer)
    {
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

        var result = await _harness.Endpoint.HandleAsync(
            HttpRequestBuilder.FromJson(dto),
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }
}
