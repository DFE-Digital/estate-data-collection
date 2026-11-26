using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.SignOffSchema;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class UnvalidatedSignOffLifecycleTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    private const string SchemaWithNumericFieldJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["urn", "quantity"],
      "properties": {
        "urn": { "type": "integer", "minimum": 1 },
        "quantity": { "type": "number", "minimum": 0, "maximum": 100 }
      }
    }
    """;

    private const string ValidFixtureJson = """{"urn": 100001, "quantity": 60}""";

    private const string WarningRulesJson = """
    [
      {
        "id": "R-CRIT-STRICT",
        "description": "Critical quantity floor",
        "engine": "cel",
        "path": "/",
        "severity": "CRITICAL",
        "implementation": "this.quantity >= 50"
      },
      {
        "id": "R-WARN-LOOSE",
        "description": "Warning quantity floor",
        "engine": "cel",
        "path": "/",
        "severity": "WARNING",
        "implementation": "this.quantity >= 20"
      }
    ]
    """;

    [Fact]
    public async Task WarningsOnly_Schema_Should_Persist_As_Unvalidated_And_Block_Scheduling()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("warn_unval");
        var submitDto = new SubmitSchemaBodyDto(
            SchemaJson: SchemaWithNumericFieldJson,
            RulesJson: WarningRulesJson,
            SampleValidFixtures: [ValidFixtureJson]);
        var scheduleDto = new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30));

        // Act
        var submitted = (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(submitDto),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);

        var prematureScheduleRes = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(scheduleDto),
            schemaName,
            1,
            CancellationToken.None);

        // Assert
        submitted.State!.Name.Should().Be("Unvalidated");
        submitted.HasErrorReport.Should().BeTrue();
        submitted.Issues.Should().ContainKey(RuleSeverity.Warning);
        prematureScheduleRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task SignOff_Unvalidated_Schema_Should_Transition_To_Validated_And_Allow_Activation()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("warn_signoff");
        var submitDto = new SubmitSchemaBodyDto(
            SchemaJson: SchemaWithNumericFieldJson,
            RulesJson: WarningRulesJson,
            SampleValidFixtures: [ValidFixtureJson]);
        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(submitDto),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);

        var scheduleDto = new ScheduleSchemaBodyDto(
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30));

        // Act
        var signOffResult = (await _harness.Get<PatchSignOffSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SignOffSchemaBodyDto("estates-lead@education.gov.uk")),
                schemaName,
                1,
                CancellationToken.None))
            .ShouldBeApiSuccess<SignOffSchemaResult>(StatusCodes.Status200OK);

        var scheduleRes = await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(scheduleDto),
            schemaName,
            1,
            CancellationToken.None);

        var pub = (await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                schemaName,
                CancellationToken.None))
            .ShouldBeApiSuccess<SchemaCatalogueEntry>(StatusCodes.Status200OK);

        // Assert
        signOffResult.State.Name.Should().Be("Validated");
        signOffResult.SignedOffBy.Should().Be("estates-lead@education.gov.uk");
        scheduleRes.ShouldHaveStatusCode(StatusCodes.Status200OK);
        pub.ActiveVersion.Should().Be(1);
    }
}
