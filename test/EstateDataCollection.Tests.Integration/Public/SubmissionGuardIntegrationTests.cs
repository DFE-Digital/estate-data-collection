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
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Finalising_Already_Finalised_Session_Should_Return_403_Forbidden()
    {
        // Arrange
        var subId = await CreateFinalisedSubmissionAsync();

        // Act
        var secondFinalise = await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), subId, CancellationToken.None);

        // Assert
        secondFinalise.ShouldHaveStatusCode(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Updating_Already_Finalised_Session_Should_Return_403_Forbidden()
    {
        // Arrange
        var subId = await CreateFinalisedSubmissionAsync();

        // Act
        var updateAfterFinalise = await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
            HttpRequestBuilder.FromRaw(SurveyPayloadFixtures.ValidPayloadJson), subId, CancellationToken.None);

        // Assert
        updateAfterFinalise.ShouldHaveStatusCode(StatusCodes.Status403Forbidden);
    }

    private async Task<Guid> CreateFinalisedSubmissionAsync()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("finalised_guard");
        await _harness.SubmitAndActivateSchemaAsync(schemaName, 1, JsonSchemaFixtures.ValidV1Schema);

        var subId = await _harness.CreateDraftSubmissionAsync(schemaName);
        (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(SurveyPayloadFixtures.ValidPayloadJson), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);
        (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        return subId;
    }
}
