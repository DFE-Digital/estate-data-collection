using Azure.Storage.Blobs;
using EstateDataCollection.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class BlobSubmissionInfrastructureTests
{
    [Fact]
    public async Task PromoteToProcessedAsync_When_Urn_Is_Zero_Should_Omit_Urn_Segment_From_Processed_Blob_Path()
    {
        var submissionId = Guid.NewGuid();
        var blobServiceClient = Substitute.For<BlobServiceClient>();
        var payloadContainer = Substitute.For<BlobContainerClient>();
        var processedContainer = Substitute.For<BlobContainerClient>();
        var draftBlob = Substitute.For<BlobClient>();
        var processedBlob = Substitute.For<BlobClient>();

        blobServiceClient.GetBlobContainerClient("payloads").Returns(payloadContainer);
        blobServiceClient.GetBlobContainerClient("processed").Returns(processedContainer);
        payloadContainer.GetBlobClient($"{submissionId}/draft.json").Returns(draftBlob);
        processedContainer.GetBlobClient(Arg.Any<string>()).Returns(processedBlob);

        var infra = new BlobSubmissionInfrastructure(
            blobServiceClient,
            Options.Create(new BlobSubmissionRepositoryOptions
            {
                PayloadContainerName = "payloads",
                ProcessedContainerName = "processed",
                ProcessedBlobNameFormat = "urn={urn}/schema={schemaName}/v={schemaVersion}/{submissionId}.json"
            }));

        await infra.PromoteToProcessedAsync(submissionId, 0, "edc-survey-elements", 1, CancellationToken.None);

        processedContainer.Received(1).GetBlobClient($"schema=edc-survey-elements/v=1/{submissionId}.json");
        processedContainer.DidNotReceive().GetBlobClient($"urn=0/schema=edc-survey-elements/v=1/{submissionId}.json");
    }
}
