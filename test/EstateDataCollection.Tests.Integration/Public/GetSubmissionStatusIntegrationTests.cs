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
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Fact]
    public async Task Status_Check_Should_Return_404_For_Unknown_Submission()
    {
        var result = await _harness.Get<GetSubmissionStatusEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(),
            Guid.NewGuid(),
            CancellationToken.None);

        result.ShouldBeApiFailure<GetSubmissionStatusResult>(StatusCodes.Status404NotFound, "Submission.NotFound");
    }

    [Fact]
    public async Task Status_Check_Should_Reflect_Lifecycle_From_Empty_Draft_To_Invalid_Draft_To_Ready_To_Finalised()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("status_check");
        await _harness.SubmitAndActivateSchemaAsync(schemaName, 1, JsonSchemaFixtures.ValidV1Schema);

        var subId = await _harness.CreateDraftSubmissionAsync(schemaName);
        var getStatus = _harness.Get<GetSubmissionStatusEndpoint>();
        var updateSubmission = _harness.Get<PutUpdateSubmissionEndpoint>();
        var finaliseSubmission = _harness.Get<PostFinaliseSubmissionEndpoint>();

        // 1. Newly created session without draft payload yet
        var emptyCheck = await getStatus.HandleAsync(
            HttpRequestBuilder.Empty(),
            subId,
            CancellationToken.None);
        var emptyStatus = emptyCheck.ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        emptyStatus.Status.Should().Be("Draft");
        emptyStatus.HasDraftPayload.Should().BeFalse();
        emptyStatus.IsReadyToFinalise.Should().BeFalse();
        emptyStatus.PendingErrors.Should().NotBeEmpty();

        // 2. Partial/invalid draft uploaded -> check catches pending errors before finalise
        var badUpdate = await updateSubmission.HandleAsync(
            HttpRequestBuilder.FromRaw(SurveyPayloadFixtures.MissingUrnJson),
            subId,
            CancellationToken.None);
        badUpdate.ShouldHaveStatusCode(StatusCodes.Status200OK);

        var invalidCheck = await getStatus.HandleAsync(
            HttpRequestBuilder.Empty(),
            subId,
            CancellationToken.None);
        var invalidStatus = invalidCheck.ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        invalidStatus.Status.Should().Be("Draft");
        invalidStatus.HasDraftPayload.Should().BeTrue();
        invalidStatus.IsReadyToFinalise.Should().BeFalse();
        invalidStatus.PendingErrors.Should().NotBeEmpty();

        // 3. Valid draft uploaded -> check confirms ready to finalise with extracted identifier
        var goodUpdate = await updateSubmission.HandleAsync(
            HttpRequestBuilder.FromRaw(SurveyPayloadFixtures.ValidPayloadJson),
            subId,
            CancellationToken.None);
        goodUpdate.ShouldHaveStatusCode(StatusCodes.Status200OK);

        var readyCheck = await getStatus.HandleAsync(
            HttpRequestBuilder.Empty(),
            subId,
            CancellationToken.None);
        var readyStatus = readyCheck.ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        readyStatus.Status.Should().Be("Draft");
        readyStatus.HasDraftPayload.Should().BeTrue();
        readyStatus.IsReadyToFinalise.Should().BeTrue();
        readyStatus.PendingErrors.Should().BeEmpty();
        readyStatus.Urn.Should().Be(104521);
        readyStatus.Identifier.Should().Be("104521");

        // 4. After finalisation -> status transitions to Finalised
        var finaliseRes = await finaliseSubmission.HandleAsync(
            HttpRequestBuilder.Empty(),
            subId,
            CancellationToken.None);
        finaliseRes.ShouldHaveStatusCode(StatusCodes.Status200OK);

        var finalisedCheck = await getStatus.HandleAsync(
            HttpRequestBuilder.Empty(),
            subId,
            CancellationToken.None);
        var finalisedStatus = finalisedCheck.ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        finalisedStatus.Status.Should().Be("Finalised");
        finalisedStatus.FinalisedAtUtc.Should().NotBeNull();
        finalisedStatus.IsReadyToFinalise.Should().BeFalse();
        finalisedStatus.PendingErrors.Should().BeEmpty();
        finalisedStatus.Urn.Should().Be(104521);
    }
}
