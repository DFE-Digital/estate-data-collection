using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class VerifyLogicEndpointTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Fact]
    public async Task When_Body_Is_Empty_Should_Return_400_BadRequest()
    {
        var result = await _harness.VerifyLogic.HandleAsync(
            HttpRequestBuilder.FromRaw("  "),
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task When_Raw_Json_Array_Of_JsonLogic_And_Cel_Rules_Posted_Should_Return_200_OK()
    {
        var rawRulesArray = new QualityRulesBuilder()
            .AddJsonLogicTimestampRule("RULE-JL-1")
            .AddCelRule("RULE-CEL-1", "school.capacity", "School capacity must be positive", "school.capacity > 0")
            .BuildJson();

        var result = await _harness.VerifyLogic.HandleAsync(
            HttpRequestBuilder.FromRaw(rawRulesArray),
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task When_Unsupported_Engine_Posted_Should_Return_422_UnprocessableEntity()
    {
        var unsupportedRules = new QualityRulesBuilder()
            .AddCustomRule("RULE-SQL-1", "dbt-sql", "school", "Unsupported SQL rule", "SELECT 1")
            .BuildJson();

        var result = await _harness.VerifyLogic.HandleAsync(
            HttpRequestBuilder.FromRaw(unsupportedRules),
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }
}
