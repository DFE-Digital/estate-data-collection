using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Admin;

[Collection("AzuriteCollection")]
public sealed class VerifySchemaSeverityAndAnalyzerTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness<PostVerifySchemaEndpoint> _harness = new(fixture);
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

    [Fact]
    public async Task VerifySchema_With_MultiSeverity_And_AnalyzerFindings_Should_Return_200_Diagnostic_Report()
    {
        // Arrange
        const string rulesJson = """
        [
          {
            "id": "R-BAD-CASING",
            "description": "Checks Quantity casing",
            "engine": "cel",
            "path": "/",
            "severity": "ERROR",
            "implementation": "this.Quantity >= 0"
          },
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
          },
          {
            "id": "R-INFO-NOTE",
            "description": "Advisory info note",
            "engine": "cel",
            "path": "/",
            "severity": "INFO",
            "message": "Advisory note"
          }
        ]
        """;

        var dto = new VerifySchemaBodyDto(
            SchemaJson: SchemaWithNumericFieldJson,
            RulesJson: rulesJson,
            SampleValidFixtures: [ValidFixtureJson],
            SampleInvalidFixtures: []);

        // Act
        var report = (await _harness.Endpoint.HandleAsync(
                HttpRequestBuilder.FromJson(dto),
                CancellationToken.None))
            .ShouldBeApiSuccess<VerifySchemaResult>(StatusCodes.Status200OK);

        // Assert
        report.IsSuccessful.Should().BeFalse();
        report.Issues.Should().ContainKey(RuleSeverity.Error);
        report.Issues.Should().ContainKey(RuleSeverity.Warning);
        report.AnalyzerFindings.Should().Contain(f => f.RuleId == "R-BAD-CASING" && f.Severity == RuleSeverity.Error);
        report.AnalyzerFindings.Should().Contain(f => f.RuleId == "R-WARN-LOOSE" && f.Severity == RuleSeverity.Warning);
    }
}
