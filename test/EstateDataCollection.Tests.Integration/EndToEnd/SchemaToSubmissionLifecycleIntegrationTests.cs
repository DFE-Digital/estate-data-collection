using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.EndToEnd;

[Collection("AzuriteCollection")]
public sealed class SchemaToSubmissionLifecycleIntegrationTests(AzuriteFixture fixture)
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);

    [Fact]
    public async Task Complete_Http_Pipeline_From_Schema_Verify_To_Submission_Finalisation_Should_Succeed()
    {
        var schemaName = IntegrationEndpointHarness.UniqueName("e2e_route");
        var missingUrnPayload = new SurveyPayloadBuilder().WithoutUrn().BuildJson();
        var validPayload = new SurveyPayloadBuilder().WithUrn(104521).BuildJson();

        // Step 1: Admin POST /schemas/verify
        var verifyDto = new VerifySchemaBodyDto(
            SchemaJson: JsonSchemaFixtures.ValidV1Schema,
            SampleValidFixtures: [validPayload],
            SampleInvalidFixtures: [missingUrnPayload]);
        (await _harness.VerifySchema.HandleAsync(HttpRequestBuilder.FromJson(verifyDto), CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Step 2: Admin POST /schemas/{schemaName}/versions/{version} (Unscheduled initially)
        (await _harness.SubmitSchema.HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)),
                schemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);

        // While unscheduled: Public GET returns 404, Public POST submissions fails 422
        (await _harness.PublicGetSchemaByName.HandleAsync(HttpRequestBuilder.Empty(), schemaName, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status404NotFound);

        var prematureSubHttp = new HttpRequestBuilder().WithQuery("version", "1").Build();
        (await _harness.CreateSubmission.HandleAsync(prematureSubHttp, schemaName, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        // Step 3: Admin PATCH /schemas/{schemaName}/versions/{version}/schedule
        var effectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-5);
        var effectiveTo = DateTimeOffset.UtcNow.AddDays(90);
        var scheduleDto = new ScheduleSchemaBodyDto(
            EffectiveFromUtc: effectiveFrom,
            EffectiveToUtc: effectiveTo,
            IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true });
        (await _harness.ScheduleSchema.HandleAsync(HttpRequestBuilder.FromJson(scheduleDto), schemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Step 4: Public GET /schemas/{schemaName}/versions/1
        var detail = (await _harness.PublicGetSchemaVersionDetail.HandleAsync(
                HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None))
            .ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);
        detail.State.Name.Should().Be("Active");
        detail.IsActiveNow.Should().BeTrue();
        detail.EffectiveFromUtc.Should().BeCloseTo(effectiveFrom, TimeSpan.FromSeconds(1));
        detail.EffectiveToUtc.Should().BeCloseTo(effectiveTo, TimeSpan.FromSeconds(1));
        detail.IdentifierConfig!.JsonPointer.Should().Be("/urn");
        detail.Fields.Should().Contain(f => f.Name == "urn" && f.Required);

        // Step 5: Public POST /submissions/{schemaName}?version=1
        var submissionId = await _harness.CreateDraftSubmissionAsync(schemaName, version: 1);

        // Step 6: Public PUT partial draft (missing URN) -> 200 OK with PendingErrors, Finalise -> 422
        var draftData = (await _harness.UpdateSubmission.HandleAsync(
                HttpRequestBuilder.FromRaw(missingUrnPayload), submissionId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        draftData.IsSuccessful.Should().BeFalse();
        draftData.PendingErrors.Should().NotBeEmpty();

        (await _harness.FinaliseSubmission.HandleAsync(HttpRequestBuilder.Empty(), submissionId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        // Step 7: Public PUT valid draft & POST finalise -> 200 OK promoted with URN
        var validDraftData = (await _harness.UpdateSubmission.HandleAsync(
                HttpRequestBuilder.FromRaw(validPayload), submissionId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        validDraftData.IsSuccessful.Should().BeTrue();
        validDraftData.PendingErrors.Should().BeEmpty();

        var finaliseData = (await _harness.FinaliseSubmission.HandleAsync(
                HttpRequestBuilder.Empty(), submissionId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);
        finaliseData.Urn.Should().Be(104521);
        finaliseData.ProcessedBlobUri.Should().Contain("urn%3D104521");
    }
}
