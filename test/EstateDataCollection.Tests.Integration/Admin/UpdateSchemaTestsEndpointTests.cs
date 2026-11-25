using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class UpdateSchemaTestsEndpointTests(AzuriteFixture fixture)
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
        "urn": { "type": "integer" },
        "capacity": { "type": "integer" }
      }
    }
    """;

    private const string CapacityRulesJson = """
    [
      {
        "id": "min-capacity",
        "description": "Capacity must be >= 20",
        "severity": "ERROR",
        "path": "/",
        "message": "Capacity must be at least 20",
        "assert": { ">=": [ { "var": "capacity" }, 20 ] }
      }
    ]
    """;

    [Fact]
    public async Task Put_UpdateSchemaTests_When_ValidFixture_Fails_Rule_Should_Return_422_Without_Overwriting()
    {
        // Arrange
        var schemaName = await SubmitCapacitySchemaAsync();
        var request = HttpRequestBuilder.FromJson(new
        {
            validFixtures = new[] { """{"urn":123456,"capacity":5}""" },
            invalidFixtures = Array.Empty<string>()
        });

        // Act
        var invalidUpdateRes = await _harness.Get<PutUpdateSchemaTestsEndpoint>().HandleAsync(
            request, schemaName, 1, CancellationToken.None);
        var unchanged = (await _harness.Get<ISchemaRegistry>().GetExplicitSchemaAsync(schemaName, 1)).ShouldSucceed();

        // Assert
        invalidUpdateRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
        unchanged.ValidFixtures.Should().ContainSingle(f => f.Contains("\"capacity\":25") || f.Contains("\"capacity\": 25"));
        unchanged.InvalidFixtures.Should().BeEmpty();
    }

    [Fact]
    public async Task Put_UpdateSchemaTests_With_Valid_Fixtures_Should_Save_And_Execute_On_RunSchemaTests()
    {
        // Arrange
        var schemaName = await SubmitCapacitySchemaAsync();
        var request = HttpRequestBuilder.FromJson(new
        {
            validFixtures = new[] { """{"urn":123456,"capacity":30}""", """{"urn":654321,"capacity":40}""" },
            invalidFixtures = new[] { """{"urn":123456,"capacity":10}""" }
        });

        // Act
        var validUpdateRes = await _harness.Get<PutUpdateSchemaTestsEndpoint>().HandleAsync(
            request, schemaName, 1, CancellationToken.None);
        var runRes = await _harness.Get<PostRunSchemaTestsEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new { schemaName, version = 1, includeRetired = true }),
            CancellationToken.None);

        // Assert
        var updated = validUpdateRes.ShouldBeApiSuccess<UpdateSchemaTestsResult>(StatusCodes.Status200OK);
        updated.ValidFixtureCount.Should().Be(2);
        updated.InvalidFixtureCount.Should().Be(1);
        updated.BusinessRuleCount.Should().Be(1);

        var runResult = runRes.ShouldBeApiSuccess<RunSchemaTestsResult>(StatusCodes.Status200OK);
        runResult.PassedCount.Should().Be(1);
        runResult.Results.Should().ContainSingle(r => r.ValidFixtureCount == 2 && r.InvalidFixtureCount == 1);
    }

    private async Task<string> SubmitCapacitySchemaAsync()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("api_tests");
        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: CapacitySchemaJson,
                    RulesJson: CapacityRulesJson,
                    SampleValidFixtures: ["""{"urn":123456,"capacity":25}"""],
                    SampleInvalidFixtures: [])),
                schemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);
        return schemaName;
    }
}
