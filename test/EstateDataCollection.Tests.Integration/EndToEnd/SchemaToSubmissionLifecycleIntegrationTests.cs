using AwesomeAssertions;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
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
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Unscheduled_Submitted_Schema_Should_Verify_Be_Hidden_From_Public_And_Block_Submissions()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("e2e_unsched");
        var missingUrnPayload = new SurveyPayloadBuilder().WithoutUrn().BuildJson();
        var validPayload = new SurveyPayloadBuilder().WithUrn(104521).BuildJson();

        var verifyDto = new VerifySchemaBodyDto(
            SchemaJson: JsonSchemaFixtures.ValidV1Schema,
            SampleValidFixtures: [validPayload],
            SampleInvalidFixtures: [missingUrnPayload]);

        // Act
        var verifyRes = await _harness.Get<PostVerifySchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(verifyDto), CancellationToken.None);
        var submitRes = await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
            HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)),
            schemaName, 1, CancellationToken.None);
        var publicSchemaRes = await _harness.Get<GetSchemaByNameEndpoint>().HandleAsync(
            HttpRequestBuilder.Empty(), schemaName, CancellationToken.None);
        var createSubmissionRes = await _harness.Get<PostCreateSubmissionEndpoint>().HandleAsync(
            new HttpRequestBuilder().WithQuery("version", "1").Build(), schemaName, CancellationToken.None);

        // Assert
        verifyRes.ShouldHaveStatusCode(StatusCodes.Status200OK);
        submitRes.ShouldHaveStatusCode(StatusCodes.Status201Created);
        publicSchemaRes.ShouldHaveStatusCode(StatusCodes.Status404NotFound);
        createSubmissionRes.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task Scheduled_Schema_Should_Expose_Active_Detail_And_Complete_Draft_To_Finalisation_Pipeline()
    {
        // Arrange
        var schemaName = IntegrationEndpointHarness.UniqueName("e2e_route");
        var missingUrnPayload = new SurveyPayloadBuilder().WithoutUrn().BuildJson();
        var validPayload = new SurveyPayloadBuilder().WithUrn(104521).BuildJson();

        (await _harness.Get<PostSubmitSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: JsonSchemaFixtures.ValidV1Schema)),
                schemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status201Created);

        var effectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-5);
        var effectiveTo = DateTimeOffset.UtcNow.AddDays(90);
        var scheduleDto = new ScheduleSchemaBodyDto(
            EffectiveFromUtc: effectiveFrom,
            EffectiveToUtc: effectiveTo,
            IdentifierConfig: new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/urn", Required = true });

        (await _harness.Get<PatchScheduleSchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(scheduleDto), schemaName, 1, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        // Act
        var detail = (await _harness.Get<GetSchemaVersionDetailEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), schemaName, 1, CancellationToken.None))
            .ShouldBeApiSuccess<SchemaVersionDetail>(StatusCodes.Status200OK);

        var submissionId = await _harness.CreateDraftSubmissionAsync(schemaName, version: 1);
        var updateSubmission = _harness.Get<PutUpdateSubmissionEndpoint>();
        var finaliseSubmission = _harness.Get<PostFinaliseSubmissionEndpoint>();

        var badDraft = (await updateSubmission.HandleAsync(
                HttpRequestBuilder.FromRaw(missingUrnPayload), submissionId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        var prematureFinalise = await finaliseSubmission.HandleAsync(
            HttpRequestBuilder.Empty(), submissionId, CancellationToken.None);

        var validDraft = (await updateSubmission.HandleAsync(
                HttpRequestBuilder.FromRaw(validPayload), submissionId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        var finaliseData = (await finaliseSubmission.HandleAsync(
                HttpRequestBuilder.Empty(), submissionId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);

        // Assert
        detail.State.Name.Should().Be("Active");
        detail.IsActiveNow.Should().BeTrue();
        detail.EffectiveFromUtc.Should().BeCloseTo(effectiveFrom, TimeSpan.FromSeconds(1));
        detail.EffectiveToUtc.Should().BeCloseTo(effectiveTo, TimeSpan.FromSeconds(1));
        detail.IdentifierConfig!.JsonPointer.Should().Be("/urn");
        detail.Fields.Should().Contain(f => f.Name == "urn" && f.Required);

        badDraft.IsSuccessful.Should().BeFalse();
        badDraft.PendingErrors.Should().NotBeEmpty();
        prematureFinalise.ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        validDraft.IsSuccessful.Should().BeTrue();
        validDraft.PendingErrors.Should().BeEmpty();
        finaliseData.Identifier.Should().Be("104521");
        finaliseData.IdentifierType.Should().Be("URN");
        finaliseData.ProcessedBlobUri.Should().Contain("urn%3D104521");
    }
}
