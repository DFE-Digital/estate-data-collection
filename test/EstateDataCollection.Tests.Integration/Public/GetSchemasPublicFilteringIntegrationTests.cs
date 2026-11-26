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
    public async Task Default_Public_Endpoints_Should_Only_Expose_Active_Version_Even_With_IncludeUnscheduled()
    {
        // Arrange
        var schemaName = await SeedFourLifecycleVersionsAsync();
        var spoofingHttp = new HttpRequestBuilder().WithQuery("includeUnscheduled", "true").Build();

        // Act
        var allDefault = (await _harness.Get<GetSchemasEndpoint>().HandleAsync(
                new HttpRequestBuilder().WithQuery("includeUnscheduled", "true").Build(),
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSchemasResult>(StatusCodes.Status200OK);
        var byNameDefault = (await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
                spoofingHttp, schemaName, CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);
        var detailV1 = await _harness.Get<GetSchemaVersionDetailEndpoint>().HandleAsync(
            new HttpRequestBuilder().WithQuery("includeUnscheduled", "true").Build(), schemaName, 1, CancellationToken.None);
        var detailV3 = await _harness.Get<GetSchemaVersionDetailEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 3, CancellationToken.None);
        var detailV4 = await _harness.Get<GetSchemaVersionDetailEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, 4, CancellationToken.None);

        // Assert
        allDefault.Schemas.Single(s => s.SchemaName == schemaName)
            .Versions.Select(v => v.Version).Should().BeEquivalentTo([2]);
        byNameDefault.Versions.Select(v => v.Version).Should().BeEquivalentTo([2]);
        detailV1.ShouldHaveStatusCode(StatusCodes.Status404NotFound);
        detailV3.ShouldHaveStatusCode(StatusCodes.Status404NotFound);
        detailV4.ShouldHaveStatusCode(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task IncludeUpcoming_True_Should_Expose_Active_And_Upcoming_Versions_Only()
    {
        // Arrange
        var schemaName = await SeedFourLifecycleVersionsAsync();

        // Act
        var byNameUpcoming = (await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
                new HttpRequestBuilder().WithQuery("includeUpcoming", "true").Build(),
                schemaName,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);

        var detailV3 = await _harness.Get<GetSchemaVersionDetailEndpoint>().HandleAsync(
            new HttpRequestBuilder().WithQuery("includeUpcoming", "true").Build(),
            schemaName,
            3,
            CancellationToken.None);

        // Assert
        byNameUpcoming.Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 3]);
        detailV3.ShouldHaveStatusCode(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task IncludeRetired_True_Should_Expose_Active_And_Retired_Versions_Only()
    {
        // Arrange
        var schemaName = await SeedFourLifecycleVersionsAsync();

        // Act
        var byNameRetired = (await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
                new HttpRequestBuilder().WithQuery("includeRetired", "true").Build(),
                schemaName,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);

        var detailV4 = await _harness.Get<GetSchemaVersionDetailEndpoint>().HandleAsync(
            new HttpRequestBuilder().WithQuery("includeRetired", "true").Build(),
            schemaName,
            4,
            CancellationToken.None);

        // Assert
        byNameRetired.Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 4]);
        detailV4.ShouldHaveStatusCode(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task IncludeUpcoming_And_IncludeRetired_Together_Should_Expose_Scheduled_Versions_And_Exclude_Unscheduled()
    {
        // Arrange
        var schemaName = await SeedFourLifecycleVersionsAsync();
        var request = new HttpRequestBuilder()
            .WithQuery("includeUpcoming", "true")
            .WithQuery("includeRetired", "true")
            .Build();

        // Act
        var byNameBoth = (await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
                request,
                schemaName,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);

        // Assert
        byNameBoth.Versions.Select(v => v.Version).Should().BeEquivalentTo([2, 3, 4]);
    }

    private async Task<string> SeedFourLifecycleVersionsAsync()
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

        // v3: Upcoming
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

        return schemaName;
    }
}
