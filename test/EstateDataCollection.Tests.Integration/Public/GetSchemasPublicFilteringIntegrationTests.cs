using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Public;

[Collection("AzuriteCollection")]
public sealed class GetSchemasPublicFilteringIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Public_Schema_Endpoints_Should_Only_Expose_Active_By_Default_And_Allow_Filtering_Upcoming_Or_Retired()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("pub_filter");
        var submit = _harness.Get<PostSubmitSchemaEndpoint>();
        var schedule = _harness.Get<PatchScheduleSchemaEndpoint>();

        // v1: Unscheduled (Validated)
        (await submit.HandleAsync(HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)), schemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);

        // v2: Active
        (await submit.HandleAsync(HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)), schemaName, 2, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);
        (await schedule.HandleAsync(
                HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                    EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                    EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30),
                    LifecycleAction: "Activate")),
                schemaName, 2, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // v3: Upcoming (Scheduled in future)
        (await submit.HandleAsync(HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)), schemaName, 3, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);
        (await schedule.HandleAsync(
                HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                    EffectiveFromUtc: DateTimeOffset.UtcNow.AddDays(10),
                    EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(60),
                    LifecycleAction: "Schedule")),
                schemaName, 3, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // v4: Retired
        (await submit.HandleAsync(HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)), schemaName, 4, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);
        (await schedule.HandleAsync(
                HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(LifecycleAction: "Retire")),
                schemaName, 4, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        var getAll = _harness.Get<GetSchemasEndpoint>();
        var getByName = _harness.Get<GetSchemaByNameEndpoint>();
        var getDetail = _harness.Get<GetSchemaVersionDetailEndpoint>();

        // 1. Default GET /schemas & GET /schemas/{name} -> ONLY Active (v2), even if caller passes includeUnscheduled=true
        var spoofingHttp = new HttpRequestBuilder().WithQuery("includeUnscheduled", "true").Build();
        var allDefault = (await getAll.HandleAsync(spoofingHttp, CancellationToken.None))
            .ShouldBeApiSuccess<GetSchemasResult>(StatusCodes.Status200OK);
        allDefault.Schemas.Single(s => s.SchemaName == schemaName)
            .Versions.Select(v => v.Version).Should().BeEquivalentTo([2]);

        var byNameDefault = (await getByName.HandleAsync(
                new HttpRequestBuilder().WithQuery("includeUnscheduled", "true").Build(),
                schemaName,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);
        byNameDefault.Versions.Select(v => v.Version).Should().BeEquivalentTo([2]);

        // 2. Detail on v1 (unscheduled), v3 (upcoming), v4 (retired) -> 404 by default
        (await getDetail.HandleAsync(new HttpRequestBuilder().WithQuery("includeUnscheduled", "true").Build(), schemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status404NotFound);
        (await getDetail.HandleAsync(HttpRequestBuilder.Empty(), schemaName, 3, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status404NotFound);
        (await getDetail.HandleAsync(HttpRequestBuilder.Empty(), schemaName, 4, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status404NotFound);

        // 3. includeUpcoming=true -> exposes v2 + v3 (and v3 detail), NOT v1 or v4
        var byNameUpcoming = (await getByName.HandleAsync(
                new HttpRequestBuilder().WithQuery("includeUpcoming", "true").Build(),
                schemaName,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);
        byNameUpcoming.Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 3]);

        (await getDetail.HandleAsync(new HttpRequestBuilder().WithQuery("includeUpcoming", "true").Build(), schemaName, 3, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // 4. includeRetired=true -> exposes v2 + v4 (and v4 detail), NOT v1 or v3
        var byNameRetired = (await getByName.HandleAsync(
                new HttpRequestBuilder().WithQuery("includeRetired", "true").Build(),
                schemaName,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);
        byNameRetired.Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 4]);

        (await getDetail.HandleAsync(new HttpRequestBuilder().WithQuery("includeRetired", "true").Build(), schemaName, 4, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // 5. Both includeUpcoming=true & includeRetired=true -> v2, v3, v4 (still never v1)
        var byNameBoth = (await getByName.HandleAsync(
                new HttpRequestBuilder().WithQuery("includeUpcoming", "true").WithQuery("includeRetired", "true").Build(),
                schemaName,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);
        byNameBoth.Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 3, 4]);
    }
}
