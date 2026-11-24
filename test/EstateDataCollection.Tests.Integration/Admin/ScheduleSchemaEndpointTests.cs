using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class ScheduleSchemaEndpointTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("")]
    [InlineData("{malformed-json")]
    public async Task When_Body_Is_Empty_Or_Malformed_Should_Return_400_BadRequest(string rawBody)
    {
        var result = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
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

        var res = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
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
        var scheduleSchema = _harness.Get<PatchScheduleSchemaEndpoint>();

        var badActionRes = await scheduleSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(LifecycleAction: "InvalidAction")),
            schemaName,
            1,
            CancellationToken.None);
        badActionRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        var now = DateTimeOffset.UtcNow;
        var invertedRes = await scheduleSchema.HandleAsync(
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
        var scheduleSchema = _harness.Get<PatchScheduleSchemaEndpoint>();

        var retireRes = await scheduleSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(LifecycleAction: "Retire")),
            schemaName,
            1,
            CancellationToken.None);
        retireRes.ShouldHaveStatusCode(StatusCodes.Status200OK);

        var rescheduleRes = await scheduleSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                EffectiveFromUtc: DateTimeOffset.UtcNow,
                LifecycleAction: "Activate")),
            schemaName,
            1,
            CancellationToken.None);
        rescheduleRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_Scheduling_With_Invalid_IdentifierPointer_Should_Return_422_InvalidIdentifierConfig()
    {
        var schemaName = await SubmitValidV1Async("sched_bad_ptr");

        var res = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
                LifecycleAction: "Activate",
                IdentifierConfig: new SchemaIdentifierConfig
                {
                    Type = "URN",
                    JsonPointer = "/missing/urn"
                })),
            schemaName,
            1,
            CancellationToken.None);

        res.ShouldBeApiFailure<ScheduleSchemaResult>(
            StatusCodes.Status422UnprocessableEntity,
            "Schema.InvalidIdentifierConfig");
    }

    [Fact]
    public async Task When_Scheduling_With_Optional_Schema_Property_Should_Derive_Required_False_Even_If_Request_Sets_True()
    {
        const string optionalIdSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["school_name"],
          "properties": {
            "school_name": { "type": "string" },
            "establishment_id": { "type": "string" }
          }
        }
        """;

        var schemaName = IntegrationEndpointHarness.UniqueName("sched_opt_id");
        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(
                    SchemaJson: optionalIdSchemaJson,
                    IdentifierConfig: new SchemaIdentifierConfig
                    {
                        Type = "LAEstab",
                        JsonPointer = "/establishment_id"
                    })),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);

        var schedRes = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
                LifecycleAction: "Activate",
                IdentifierConfig: new SchemaIdentifierConfig
                {
                    Type = "LAEstab",
                    JsonPointer = "/establishment_id",
                    Required = true
                })),
            schemaName,
            1,
            CancellationToken.None);

        var scheduled = schedRes.ShouldBeApiSuccess<ScheduleSchemaResult>(StatusCodes.Status200OK);
        scheduled.IdentifierConfig.Should().NotBeNull();
        scheduled.IdentifierConfig!.Type.Should().Be("LAEstab");
        scheduled.IdentifierConfig.JsonPointer.Should().Be("/establishment_id");
        scheduled.IdentifierConfig.Required.Should().BeFalse();

        var detail = (await _harness.Get<GetAdminSchemaVersionDetailEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);
        detail.IdentifierConfig.Should().NotBeNull();
        detail.IdentifierConfig!.Required.Should().BeFalse();
    }

    private async Task<string> SubmitValidV1Async(string prefix)
    {
        var schemaName = IntegrationEndpointHarness.UniqueName(prefix);
        var res = await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)),
            schemaName,
            1,
            CancellationToken.None);
        res.ShouldHaveStatusCode(StatusCodes.Status201Created);
        return schemaName;
    }
}
