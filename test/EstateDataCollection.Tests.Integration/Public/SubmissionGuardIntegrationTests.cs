using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Public;

[Collection("AzuriteCollection")]
public sealed class SubmissionGuardIntegrationTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Fact]
    public async Task Updating_Or_Finalising_Already_Finalised_Session_Should_Return_403_Forbidden()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("finalised_guard");
        await _harness.SubmitAndActivateSchemaAsync(schemaName, 1, JsonSchemaFixtures.ValidV1Schema);

        var subId = await _harness.CreateDraftSubmissionAsync(schemaName);
        var updateSubmission = _harness.Get<PutUpdateSubmissionEndpoint>();
        var finaliseSubmission = _harness.Get<PostFinaliseSubmissionEndpoint>();

        var updateRes = await updateSubmission.HandleAsync(
            HttpRequestBuilder.FromRaw(SurveyPayloadFixtures.ValidPayloadJson),
            subId,
            CancellationToken.None);
        updateRes.ShouldHaveStatusCode(StatusCodes.Status200OK);

        var firstFinalise = await finaliseSubmission.HandleAsync(
            HttpRequestBuilder.Empty(),
            subId,
            CancellationToken.None);
        firstFinalise.ShouldHaveStatusCode(StatusCodes.Status200OK);

        var secondFinalise = await finaliseSubmission.HandleAsync(
            HttpRequestBuilder.Empty(),
            subId,
            CancellationToken.None);
        secondFinalise.ShouldHaveStatusCode(StatusCodes.Status403Forbidden);

        var updateAfterFinalise = await updateSubmission.HandleAsync(
            HttpRequestBuilder.FromRaw(SurveyPayloadFixtures.ValidPayloadJson),
            subId,
            CancellationToken.None);
        updateAfterFinalise.ShouldHaveStatusCode(StatusCodes.Status403Forbidden);
    }
}
