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
        // Arrange
        var request = HttpRequestBuilder.FromRaw(rawBody);

        // Act
        var result = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            request, "any_schema", 1, CancellationToken.None);

        // Assert
        result.ShouldHaveStatusCode(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task When_Payload_Has_No_Schedule_Or_Config_Should_Return_422_UnprocessableEntity()
    {
        // Arrange
        var schemaName = await SubmitValidV1Async("sched_empty");
        var request = HttpRequestBuilder.FromRaw("{}");

        // Act
        var result = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            request, schemaName, 1, CancellationToken.None);

        // Assert
        result.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_Unsupported_LifecycleAction_Should_Return_422_UnprocessableEntity()
    {
        // Arrange
        var schemaName = await SubmitValidV1Async("sched_bad_act");
        var request = HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(LifecycleAction: "InvalidAction"));

        // Act
        var result = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            request, schemaName, 1, CancellationToken.None);

        // Assert
        result.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_Effective_Dates_Are_Inverted_Should_Return_422_UnprocessableEntity()
    {
        // Arrange
        var schemaName = await SubmitValidV1Async("sched_inv_dates");
        var now = DateTimeOffset.UtcNow;
        var request = HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
            EffectiveFromUtc: now.AddDays(10),
            EffectiveToUtc: now.AddDays(5),
            LifecycleAction: "Schedule"));

        // Act
        var result = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            request, schemaName, 1, CancellationToken.None);

        // Assert
        result.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_Schema_Is_Retired_Attempting_To_Reschedule_Should_Return_422_UnprocessableEntity()
    {
        // Arrange
        var schemaName = await SubmitValidV1Async("sched_retire");
        var scheduleSchema = _harness.Get<PatchScheduleSchemaEndpoint>();

        (await scheduleSchema.HandleAsync(
                HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(LifecycleAction: "Retire")),
                schemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        var rescheduleReq = HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow,
            LifecycleAction: "Activate"));

        // Act
        var rescheduleRes = await scheduleSchema.HandleAsync(
            rescheduleReq, schemaName, 1, CancellationToken.None);

        // Assert
        rescheduleRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task When_Scheduling_With_Invalid_IdentifierPointer_Should_Return_422_InvalidIdentifierConfig()
    {
        // Arrange
        var schemaName = await SubmitValidV1Async("sched_bad_ptr");
        var request = HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            LifecycleAction: "Activate",
            IdentifierConfig: new SchemaIdentifierConfig
            {
                Type = "URN",
                JsonPointer = "/missing/urn"
            }));

        // Act
        var result = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            request, schemaName, 1, CancellationToken.None);

        // Assert
        result.ShouldBeApiFailure<ScheduleSchemaResult>(
            StatusCodes.Status422UnprocessableEntity,
            "Schema.InvalidIdentifierConfig");
    }

    [Fact]
    public async Task When_Scheduling_With_Optional_Schema_Property_Should_Derive_Required_False()
    {
        // Arrange
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
                schemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);

        var scheduleRequest = HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            LifecycleAction: "Activate",
            IdentifierConfig: new SchemaIdentifierConfig
            {
                Type = "LAEstab",
                JsonPointer = "/establishment_id",
                Required = true
            }));

        // Act
        var schedRes = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            scheduleRequest, schemaName, 1, CancellationToken.None);
        var detail = (await _harness.Get<GetAdminSchemaVersionDetailEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None))
            .ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);

        // Assert
        var scheduled = schedRes.ShouldBeApiSuccess<ScheduleSchemaResult>(StatusCodes.Status200OK);
        scheduled.IdentifierConfig.Should().NotBeNull();
        scheduled.IdentifierConfig!.Type.Should().Be("LAEstab");
        scheduled.IdentifierConfig.JsonPointer.Should().Be("/establishment_id");
        scheduled.IdentifierConfig.Required.Should().BeFalse();

        detail.IdentifierConfig.Should().NotBeNull();
        detail.IdentifierConfig!.Required.Should().BeFalse();
    }

    private async Task<string> SubmitValidV1Async(string prefix)
    {
        var schemaName = IntegrationEndpointHarness.UniqueName(prefix);
        var res = await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)),
            schemaName, 1, CancellationToken.None);
        res.ShouldHaveStatusCode(StatusCodes.Status201Created);
        return schemaName;
    }
}
