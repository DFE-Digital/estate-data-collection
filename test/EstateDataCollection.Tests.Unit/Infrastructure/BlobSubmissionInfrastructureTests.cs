using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AwesomeAssertions;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Tests.Common.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public class BlobSubmissionInfrastructureTests
{
    [Fact]
    public async Task PromoteToProcessedAsync_When_Identifier_Is_Unassigned_Should_Omit_Identifier_Segment_From_Processed_Blob_Path()
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

        await infra.PromoteToProcessedAsync(submissionId, null, null, "edc-survey-elements", 1, CancellationToken.None);

        processedContainer.Received(1).GetBlobClient($"schema=edc-survey-elements/v=1/{submissionId}.json");
        processedContainer.DidNotReceive().GetBlobClient($"urn=0/schema=edc-survey-elements/v=1/{submissionId}.json");
    }

    [Fact]
    public async Task PromoteToProcessedAsync_With_Custom_Identifier_Should_Partition_By_Type_And_Value()
    {
        var submissionId = Guid.NewGuid();
        var blobServiceClient = Substitute.For<BlobServiceClient>();
        var payloadContainer = Substitute.For<BlobContainerClient>();
        var processedContainer = Substitute.For<BlobContainerClient>();
        var draftBlob = Substitute.For<BlobClient>();
        var sessionBlob = Substitute.For<BlobClient>();
        var processedBlob = Substitute.For<BlobClient>();

        draftBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, Substitute.For<Response>()));
        draftBlob.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                BlobsModelFactory.BlobDownloadResult(BinaryData.FromString("""{"establishment":{"laestab":"201-3614"}}""")),
                Substitute.For<Response>()));

        sessionBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(false, Substitute.For<Response>()));

        processedBlob.Uri.Returns(new Uri($"https://storage.local/processed/laestab=201-3614/schema=edc-estab/v=1/{submissionId}.json"));

        blobServiceClient.GetBlobContainerClient("payloads").Returns(payloadContainer);
        blobServiceClient.GetBlobContainerClient("processed").Returns(processedContainer);
        payloadContainer.GetBlobClient($"{submissionId}/draft.json").Returns(draftBlob);
        payloadContainer.GetBlobClient($"{submissionId}/session.json").Returns(sessionBlob);
        processedContainer.GetBlobClient(Arg.Any<string>()).Returns(processedBlob);

        var infra = new BlobSubmissionInfrastructure(
            blobServiceClient,
            Options.Create(new BlobSubmissionRepositoryOptions
            {
                PayloadContainerName = "payloads",
                ProcessedContainerName = "processed",
                ProcessedBlobNameFormat = "urn={urn}/schema={schemaName}/v={schemaVersion}/{submissionId}.json"
            }));

        var uri = (await infra.PromoteToProcessedAsync(
            submissionId,
            "LAEstab",
            "201-3614",
            "edc-estab",
            1,
            CancellationToken.None)).ShouldSucceed();

        uri.Should().Contain("laestab=201-3614");
        processedContainer.Received(1).GetBlobClient($"laestab=201-3614/schema=edc-estab/v=1/{submissionId}.json");
        await processedBlob.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            Arg.Is<BlobUploadOptions>(o =>
                o.Metadata["identifier"] == "201-3614" &&
                o.Metadata["identifier_type"] == "LAEstab"),
            Arg.Any<CancellationToken>());
    }
}
