using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.EndToEnd;

[Collection("AzuriteCollection")]
public sealed class NonUrnSubmissionPromotionIntegrationTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Fact]
    public async Task Finalising_NonUrn_Schema_Should_Promote_Without_Urn_Prefix_In_Blob_Path()
    {
        var nonUrnSchemaName = IntegrationEndpointHarness.UniqueName("edc_elements");
        const string elementsSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["survey", "elements"],
          "properties": {
            "survey": {
              "type": "object",
              "required": ["id"],
              "properties": { "id": { "type": "string" } }
            },
            "elements": { "type": "array" }
          }
        }
        """;

        await _harness.SubmitAndActivateSchemaAsync(
            nonUrnSchemaName,
            1,
            elementsSchemaJson,
            new SchemaIdentifierConfig { Type = "None", JsonPointer = null, Required = false });

        var subId = await _harness.CreateDraftSubmissionAsync(nonUrnSchemaName);

        const string validElementsPayload = """{"survey":{"id":"SURV-999"},"elements":[]}""";
        (await _harness.UpdateSubmission.HandleAsync(
                HttpRequestBuilder.FromRaw(validElementsPayload), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        var finaliseData = (await _harness.FinaliseSubmission.HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        finaliseData.Urn.Should().Be(0);
        finaliseData.ProcessedBlobUri.Should().NotContain("urn");
        finaliseData.ProcessedBlobUri.Should().Contain($"schema%3D{nonUrnSchemaName}/v%3D1/{subId}.json");
    }
}
