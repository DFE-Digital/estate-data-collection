using AwesomeAssertions;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.Public;

[Collection("AzuriteCollection")]
public sealed class GetSubmissionStatusIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Status_Check_Should_Return_404_For_Unknown_Submission()
    {
        // Arrange
        var unknownSubmissionId = Guid.NewGuid();

        // Act
        var result = await _harness.Get<GetSubmissionStatusEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(),
            unknownSubmissionId,
            CancellationToken.None);

        // Assert
        result.ShouldBeApiFailure<GetSubmissionStatusResult>(StatusCodes.Status404NotFound, "Submission.NotFound");
    }

    [Fact]
    public async Task Status_Check_On_Empty_Draft_Should_Report_Draft_NotReady_With_PendingErrors()
    {
        // Arrange
        var subId = await CreateActiveSubmissionSessionAsync();

        // Act
        var status = (await _harness.Get<GetSubmissionStatusEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);

        // Assert
        status.Status.Should().Be("Draft");
        status.HasDraftPayload.Should().BeFalse();
        status.IsReadyToFinalise.Should().BeFalse();
        status.PendingErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Status_Check_On_Invalid_Draft_Should_Report_PendingErrors()
    {
        // Arrange
        var subId = await CreateActiveSubmissionSessionAsync();
        (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(SurveyPayloadFixtures.MissingUrnJson),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Act
        var status = (await _harness.Get<GetSubmissionStatusEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);

        // Assert
        status.Status.Should().Be("Draft");
        status.HasDraftPayload.Should().BeTrue();
        status.IsReadyToFinalise.Should().BeFalse();
        status.PendingErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Status_Check_On_Valid_Draft_Should_Report_ReadyToFinalise_With_Identifier()
    {
        // Arrange
        var subId = await CreateActiveSubmissionSessionAsync();
        (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(SurveyPayloadFixtures.ValidPayloadJson),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Act
        var status = (await _harness.Get<GetSubmissionStatusEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);

        // Assert
        status.Status.Should().Be("Draft");
        status.HasDraftPayload.Should().BeTrue();
        status.IsReadyToFinalise.Should().BeTrue();
        status.PendingErrors.Should().BeEmpty();
        status.Identifier.Should().Be("104521");
        status.IdentifierType.Should().Be("URN");
    }

    [Fact]
    public async Task Status_Check_After_Finalisation_Should_Report_Finalised_State()
    {
        // Arrange
        var subId = await CreateActiveSubmissionSessionAsync();
        (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(SurveyPayloadFixtures.ValidPayloadJson),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);
        (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Act
        var status = (await _harness.Get<GetSubmissionStatusEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(),
                subId,
                CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);

        // Assert
        status.Status.Should().Be("Finalised");
        status.FinalisedAtUtc.Should().NotBeNull();
        status.IsReadyToFinalise.Should().BeFalse();
        status.PendingErrors.Should().BeEmpty();
        status.Identifier.Should().Be("104521");
        status.IdentifierType.Should().Be("URN");
    }

    private async Task<Guid> CreateActiveSubmissionSessionAsync()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("status_check");
        await _harness.SubmitAndActivateSchemaAsync(schemaName, 1, JsonSchemaFixtures.ValidV1Schema);
        return await _harness.CreateDraftSubmissionAsync(schemaName);
    }
}
