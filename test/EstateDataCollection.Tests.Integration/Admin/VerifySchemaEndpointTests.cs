using AwesomeAssertions;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
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
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("""{"schemaJson": "", "rulesJson": null, "odcsYaml": "  "}""")]
    public async Task When_Body_Is_Empty_Or_Has_No_Inputs_Should_Return_400_BadRequest(string rawBody)
    {
        var result = await _harness.VerifySchema.HandleAsync(
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

        var result = await _harness.VerifySchema.HandleAsync(http, CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_Valid_Fixture_Violates_Schema_Should_Return_422_UnprocessableEntity()
    {
        var dto = new VerifySchemaBodyDto(
            SchemaJson: JsonSchemaFixtures.ValidV1Schema,
            SampleValidFixtures: [new SurveyPayloadBuilder().WithoutUrn().BuildJson()],
            SampleInvalidFixtures: []);

        var result = await _harness.VerifySchema.HandleAsync(
            HttpRequestBuilder.FromJson(dto),
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }
}
