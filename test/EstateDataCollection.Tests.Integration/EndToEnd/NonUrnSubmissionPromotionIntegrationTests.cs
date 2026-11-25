using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.EndToEnd;

[Collection("AzuriteCollection")]
public sealed class NonUrnSubmissionPromotionIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    private const string LaestabSchemaJson = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["establishment", "answers"],
      "properties": {
        "establishment": {
          "type": "object",
          "required": ["laestab"],
          "properties": { "laestab": { "type": "string" } }
        },
        "answers": { "type": "array" }
      }
    }
    """;

    [Fact]
    public async Task Finalising_NonUrn_Schema_Should_Promote_Without_Urn_Prefix_In_Blob_Path()
    {
        // Arrange
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
        (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(validElementsPayload), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Act
        var finaliseData = (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        // Assert
        finaliseData.Identifier.Should().BeNull();
        finaliseData.IdentifierType.Should().BeNull();
        finaliseData.ProcessedBlobUri.Should().NotContain("urn");
        finaliseData.ProcessedBlobUri.Should().Contain($"schema%3D{nonUrnSchemaName}/v%3D1/{subId}/payload.json");
    }

    [Fact]
    public async Task Custom_LAEstab_Draft_Missing_Identifier_Should_Flag_PendingErrors_And_Block_Finalisation()
    {
        // Arrange
        var laestabSchemaName = IntegrationEndpointHarness.UniqueName("edc_laestab");
        await _harness.SubmitAndActivateSchemaAsync(
            laestabSchemaName,
            1,
            LaestabSchemaJson,
            new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab" });

        var subId = await _harness.CreateDraftSubmissionAsync(laestabSchemaName);
        const string missingIdPayload = """{"establishment":{},"answers":[]}""";

        // Act
        var badDraft = (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(missingIdPayload), subId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        var finaliseRes = await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), subId, CancellationToken.None);

        // Assert
        badDraft.IsSuccessful.Should().BeFalse();
        badDraft.PendingErrors.Should().NotBeEmpty();
        finaliseRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task Custom_LAEstab_Valid_Identifier_Should_Promote_Under_LAEstab_Prefix()
    {
        // Arrange
        var laestabSchemaName = IntegrationEndpointHarness.UniqueName("edc_laestab_ok");
        await _harness.SubmitAndActivateSchemaAsync(
            laestabSchemaName,
            1,
            LaestabSchemaJson,
            new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab" });

        var subId = await _harness.CreateDraftSubmissionAsync(laestabSchemaName);
        const string validPayload = """{"establishment":{"laestab":"201-3614"},"answers":[]}""";

        // Act
        var validDraft = (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(validPayload), subId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);

        var finaliseData = (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        // Assert
        validDraft.IsSuccessful.Should().BeTrue();
        validDraft.PendingErrors.Should().BeEmpty();
        finaliseData.Identifier.Should().Be("201-3614");
        finaliseData.IdentifierType.Should().Be("LAEstab");
        finaliseData.ProcessedBlobUri.Should().Contain($"laestab%3D201-3614/schema%3D{laestabSchemaName}/v%3D1/{subId}/payload.json");
    }
}
