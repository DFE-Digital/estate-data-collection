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
        (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(validElementsPayload), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        var finaliseData = (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        finaliseData.Identifier.Should().BeNull();
        finaliseData.IdentifierType.Should().BeNull();
        finaliseData.ProcessedBlobUri.Should().NotContain("urn");
        finaliseData.ProcessedBlobUri.Should().Contain($"schema%3D{nonUrnSchemaName}/v%3D1/{subId}.json");
    }

    [Fact]
    public async Task Custom_LAEstab_String_Identifier_Should_Flag_PendingErrors_On_Draft_And_Promote_Under_LAEstab_Prefix()
    {
        var laestabSchemaName = IntegrationEndpointHarness.UniqueName("edc_laestab");
        const string laestabSchemaJson = """
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

        await _harness.SubmitAndActivateSchemaAsync(
            laestabSchemaName,
            1,
            laestabSchemaJson,
            new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab" });

        var subId = await _harness.CreateDraftSubmissionAsync(laestabSchemaName);
        var updateSubmission = _harness.Get<PutUpdateSubmissionEndpoint>();
        var finaliseSubmission = _harness.Get<PostFinaliseSubmissionEndpoint>();

        // 1. PUT draft missing identifier -> 200 OK with PendingErrors; Finalise -> 422
        const string missingIdPayload = """{"establishment":{},"answers":[]}""";
        var badDraft = (await updateSubmission.HandleAsync(
                HttpRequestBuilder.FromRaw(missingIdPayload), subId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        badDraft.IsSuccessful.Should().BeFalse();
        badDraft.PendingErrors.Should().NotBeEmpty();

        (await finaliseSubmission.HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        // 2. PUT valid draft with string LAEstab -> Finalise -> promoted under laestab=201-3614/...
        const string validPayload = """{"establishment":{"laestab":"201-3614"},"answers":[]}""";
        var validDraft = (await updateSubmission.HandleAsync(
                HttpRequestBuilder.FromRaw(validPayload), subId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        validDraft.IsSuccessful.Should().BeTrue();
        validDraft.PendingErrors.Should().BeEmpty();

        var finaliseData = (await finaliseSubmission.HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        finaliseData.Identifier.Should().Be("201-3614");
        finaliseData.IdentifierType.Should().Be("LAEstab");
        finaliseData.ProcessedBlobUri.Should().Contain($"laestab%3D201-3614/schema%3D{laestabSchemaName}/v%3D1/{subId}.json");
    }
}
