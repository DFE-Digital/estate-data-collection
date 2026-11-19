using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class SubmitSchemaEndpointTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness<PostSubmitSchemaEndpoint> _harness = new(fixture);

    [Theory]
    [InlineData("")]
    [InlineData("{not-valid-json")]
    [InlineData("""{"schemaJson": null, "odcsYaml": null}""")]
    public async Task When_Body_Is_Empty_Malformed_Or_Missing_Schema_Should_Return_400_BadRequest(string rawBody)
    {
        var result = await _harness.Endpoint.HandleAsync(
            HttpRequestBuilder.FromRaw(rawBody),
            "bad_submit_schema",
            1,
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task When_Same_Schema_Version_Submitted_Twice_Should_Return_409_Conflict()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("dup_schema");
        var dto = new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema);

        var first = await _harness.Endpoint.HandleAsync(
            HttpRequestBuilder.FromJson(dto),
            schemaName,
            1,
            CancellationToken.None);
        first.ShouldHaveStatusCode(StatusCodes.Status201Created);

        var duplicate = await _harness.Endpoint.HandleAsync(
            HttpRequestBuilder.FromJson(dto),
            schemaName,
            1,
            CancellationToken.None);
        duplicate.ShouldHaveStatusCode(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task When_Raw_Odcs_Yaml_Submitted_With_Urn_Headers_Should_Persist_IdentifierConfig()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("yaml_hdr");
        var rawOdcsYaml = new OdcsContractYamlBuilder()
            .WithId("edc-yaml-hdr")
            .WithName("YamlHeaderTest")
            .WithNestedSchoolUrn()
            .Build();

        var http = new HttpRequestBuilder()
            .WithRawBody(rawOdcsYaml, "application/yaml")
            .WithFileName("edc-school-site.odcs.yaml")
            .WithUrnHeaders("/school/urn", required: true)
            .Build();

        var result = await _harness.Endpoint.HandleAsync(http, schemaName, 1, CancellationToken.None);

        var data = result.ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);
        data.State.Name.Should().Be("Validated");
        data.EffectiveFromUtc.Should().BeNull();
        data.IdentifierConfig.Should().NotBeNull();
        data.IdentifierConfig!.JsonPointer.Should().Be("/school/urn");
        data.IdentifierConfig.Required.Should().BeTrue();
    }
}
