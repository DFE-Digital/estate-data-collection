using Azure.Storage.Blobs;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Fixtures;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EstateDataCollection.Tests.Integration;

[CollectionDefinition("AzuriteCollection")]
public class AzuriteCollection : ICollectionFixture<AzuriteFixture>;

[Collection("AzuriteCollection")]
public class BlobSubmissionIntegrationTests(AzuriteFixture fixture)
{
    private readonly BlobSubmissionInfrastructure _sut = new(
        fixture.Client,
        Options.Create(new BlobSubmissionRepositoryOptions
        {
            PayloadContainerName = "payloads",
            ProcessedContainerName = "processed",
            SessionBlobNameFormat = "{submissionId}/session.json",
            PayloadBlobNameFormat = "{submissionId}/draft.json",
            ProcessedBlobNameFormat = "urn={urn}/schema={schemaName}/v={schemaVersion}/{submissionId}.json"
        }));

    [Fact]
    public async Task When_Executing_Complete_Ingestion_Lifecycle_Against_Azurite_It_Should_Persist_Draft_And_Promote_To_Processed()
    {
        // 1. Create Session
        var session = SubmissionSession.Create("edc_payload", 1);
        var createResult = await _sut.CreateSessionAsync(session);
        createResult.IsSuccess.Should().BeTrue();

        // 2. Save Draft
        var draftResult = await _sut.SaveDraftPayloadAsync(session.SubmissionId, 104521, SurveyPayloadFixtures.ValidPayloadJson);
        draftResult.IsSuccess.Should().BeTrue();

        // 3. Promote to Processed
        var promoteResult = await _sut.PromoteToProcessedAsync(session.SubmissionId, 104521, "edc_payload", 1);
        promoteResult.IsSuccess.Should().BeTrue();

        // 4. Verify draft was cleaned up from payloads container
        var draftExists = await _sut.GetDraftPayloadAsync(session.SubmissionId);
        draftExists.IsFailure.Should().BeTrue();
    }
}
