using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class ScheduleSchemaEndpointTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Theory]
    [InlineData("")]
    [InlineData("{malformed-json")]
    public async Task When_Body_Is_Empty_Or_Malformed_Should_Return_400_BadRequest(string rawBody)
    {
        var result = await _harness.ScheduleSchema.HandleAsync(
            HttpRequestBuilder.FromRaw(rawBody),
            "any_schema",
            1,
            CancellationToken.None);

        result.ShouldHaveStatusCode(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task When_Payload_Has_No_Schedule_Or_Config_Should_Return_422_UnprocessableEntity()
    {
        var schemaName = await SubmitValidV1Async("sched_empty");

        var res = await _harness.ScheduleSchema.HandleAsync(
            HttpRequestBuilder.FromRaw("{}"),
            schemaName,
            1,
            CancellationToken.None);

        res.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_Unsupported_LifecycleAction_Or_Inverted_Dates_Should_Return_422_UnprocessableEntity()
    {
        var schemaName = await SubmitValidV1Async("sched_invalid");

        var badActionRes = await _harness.ScheduleSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(LifecycleAction: "InvalidAction")),
            schemaName,
            1,
            CancellationToken.None);
        badActionRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        var now = DateTimeOffset.UtcNow;
        var invertedRes = await _harness.ScheduleSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                EffectiveFromUtc: now.AddDays(10),
                EffectiveToUtc: now.AddDays(5),
                LifecycleAction: "Schedule")),
            schemaName,
            1,
            CancellationToken.None);
        invertedRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_Schema_Is_Retired_Attempting_To_Reschedule_Should_Return_422_UnprocessableEntity()
    {
        var schemaName = await SubmitValidV1Async("sched_retire");

        var retireRes = await _harness.ScheduleSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(LifecycleAction: "Retire")),
            schemaName,
            1,
            CancellationToken.None);
        retireRes.ShouldHaveStatusCode(StatusCodes.Status200OK);

        var rescheduleRes = await _harness.ScheduleSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                EffectiveFromUtc: DateTimeOffset.UtcNow,
                LifecycleAction: "Activate")),
            schemaName,
            1,
            CancellationToken.None);
        rescheduleRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    private async Task<string> SubmitValidV1Async(string prefix)
    {
        var schemaName = IntegrationEndpointHarness.UniqueName(prefix);
        var res = await _harness.SubmitSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)),
            schemaName,
            1,
            CancellationToken.None);
        res.ShouldHaveStatusCode(StatusCodes.Status201Created);
        return schemaName;
    }
}
