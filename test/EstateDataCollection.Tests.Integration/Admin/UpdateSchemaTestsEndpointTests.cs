using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Schema.RunSchemaTests;
using EstateDataCollection.Application.UseCases.Schema.UpdateSchemaTests;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class UpdateSchemaTestsEndpointTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Fact]
    public async Task Put_UpdateSchemaTests_Should_Verify_Against_Schema_And_Business_Rules_Before_Saving()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("api_tests");
        var schemaJson = """
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

        var rulesJson = """
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

        var submitRes = await _harness.SubmitSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                SchemaJson: schemaJson,
                RulesJson: rulesJson,
                SampleValidFixtures: ["""{"urn":123456,"capacity":25}"""],
                SampleInvalidFixtures: [])),
            schemaName,
            1,
            CancellationToken.None);
        submitRes.ShouldHaveStatusCode(StatusCodes.Status201Created);

        // Failing update (valid fixture breaks business rule capacity >= 20)
        var invalidUpdateRes = await _harness.UpdateSchemaTests.HandleAsync(
            HttpRequestBuilder.FromJson(new
            {
                validFixtures = new[] { """{"urn":123456,"capacity":5}""" },
                invalidFixtures = Array.Empty<string>()
            }),
            schemaName,
            1,
            CancellationToken.None);
        invalidUpdateRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        // Verify original fixture in Azurite was not overwritten
        var unchanged = (await _harness.SchemaRegistry.GetExplicitSchemaAsync(schemaName, 1)).ShouldSucceed();
        unchanged.ValidFixtures.Should().ContainSingle(f => f.Contains("\"capacity\":25") || f.Contains("\"capacity\": 25"));
        unchanged.InvalidFixtures.Should().BeEmpty();

        // Successful update adding valid + negative (business rule violation) fixtures
        var validUpdateRes = await _harness.UpdateSchemaTests.HandleAsync(
            HttpRequestBuilder.FromJson(new
            {
                validFixtures = new[] { """{"urn":123456,"capacity":30}""", """{"urn":654321,"capacity":40}""" },
                invalidFixtures = new[] { """{"urn":123456,"capacity":10}""" }
            }),
            schemaName,
            1,
            CancellationToken.None);
        var updated = validUpdateRes.ShouldBeApiSuccess<UpdateSchemaTestsResult>(StatusCodes.Status200OK);
        updated.ValidFixtureCount.Should().Be(2);
        updated.InvalidFixtureCount.Should().Be(1);
        updated.BusinessRuleCount.Should().Be(1);

        // RunSchemaTests confirms new fixtures execute cleanly
        var runRes = await _harness.RunSchemaTests.HandleAsync(
            HttpRequestBuilder.FromJson(new { schemaName, version = 1, includeRetired = true }),
            CancellationToken.None);
        var runResult = runRes.ShouldBeApiSuccess<RunSchemaTestsResult>(StatusCodes.Status200OK);
        runResult.PassedCount.Should().Be(1);
        runResult.Results.Should().ContainSingle(r => r.ValidFixtureCount == 2 && r.InvalidFixtureCount == 1);
    }
}
