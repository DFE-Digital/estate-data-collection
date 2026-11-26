using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AwesomeAssertions;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Tests.Common.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class BlobSubmissionAuditStoreTests
{
    [Fact]
    public async Task AppendAsync_When_Session_Has_ProcessedBundlePrefix_Should_Mirror_Audit_Entry_To_Processed_Bundle()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var bundlePrefix = $"urn=104521/schema=edc-site/v=1/{submissionId}/";

        var session = new SubmissionSession
        {
            SubmissionId = submissionId,
            SchemaName = "edc-site",
            SchemaVersion = 1
        };
        session.AssignIdentifier("URN", "104521");
        session.Finalise(bundlePrefix);

        var blobServiceClient = Substitute.For<BlobServiceClient>();
        var payloadContainer = Substitute.For<BlobContainerClient>();
        var processedContainer = Substitute.For<BlobContainerClient>();
        var workingAuditBlob = Substitute.For<BlobClient>();
        var workingSessionBlob = Substitute.For<BlobClient>();
        var processedAuditBlob = Substitute.For<BlobClient>();

        var expectedFileName = $"{timestamp:yyyyMMddTHHmmssfffffffZ}_{eventId:N}.json";
        var expectedWorkingPath = $"{submissionId}/audit/{expectedFileName}";
        var expectedProcessedPath = $"{bundlePrefix}audit/{expectedFileName}";

        blobServiceClient.GetBlobContainerClient("payloads").Returns(payloadContainer);
        blobServiceClient.GetBlobContainerClient("processed").Returns(processedContainer);
        payloadContainer.GetBlobClient(expectedWorkingPath).Returns(workingAuditBlob);
        payloadContainer.GetBlobClient($"{submissionId}/session.json").Returns(workingSessionBlob);
        processedContainer.GetBlobClient(expectedProcessedPath).Returns(processedAuditBlob);

        workingSessionBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, Substitute.For<Response>()));
        workingSessionBlob.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                BlobsModelFactory.BlobDownloadResult(BinaryData.FromString(JsonSerializer.Serialize(session))),
                Substitute.For<Response>()));

        var store = new BlobSubmissionAuditStore(blobServiceClient, Options.Create(new BlobSubmissionRepositoryOptions()));

        var entry = new SubmissionAuditEntry
        {
            EventId = eventId,
            SubmissionId = submissionId,
            SchemaName = "edc-site",
            SchemaVersion = 1,
            TimestampUtc = timestamp,
            Action = "FinaliseSubmission",
            Outcome = new SubmissionAuditOutcome { Status = "Succeeded", HttpStatus = 200, Message = "Promoted" }
        };

        // Act
        var result = (await store.AppendAsync(entry, CancellationToken.None)).ShouldSucceed();

        // Assert
        result.Should().Be(expectedWorkingPath);
        await workingAuditBlob.Received(1).UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>());
        await processedAuditBlob.Received(1).UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendAsync_When_Session_Is_Draft_Should_Not_Write_To_Processed_Container()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        var session = new SubmissionSession
        {
            SubmissionId = submissionId,
            SchemaName = "edc-site",
            SchemaVersion = 1
        };

        var blobServiceClient = Substitute.For<BlobServiceClient>();
        var payloadContainer = Substitute.For<BlobContainerClient>();
        var processedContainer = Substitute.For<BlobContainerClient>();
        var workingAuditBlob = Substitute.For<BlobClient>();
        var workingSessionBlob = Substitute.For<BlobClient>();

        var expectedFileName = $"{timestamp:yyyyMMddTHHmmssfffffffZ}_{eventId:N}.json";
        var expectedWorkingPath = $"{submissionId}/audit/{expectedFileName}";

        blobServiceClient.GetBlobContainerClient("payloads").Returns(payloadContainer);
        blobServiceClient.GetBlobContainerClient("processed").Returns(processedContainer);
        payloadContainer.GetBlobClient(expectedWorkingPath).Returns(workingAuditBlob);
        payloadContainer.GetBlobClient($"{submissionId}/session.json").Returns(workingSessionBlob);

        workingSessionBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, Substitute.For<Response>()));
        workingSessionBlob.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                BlobsModelFactory.BlobDownloadResult(BinaryData.FromString(JsonSerializer.Serialize(session))),
                Substitute.For<Response>()));

        var store = new BlobSubmissionAuditStore(blobServiceClient, Options.Create(new BlobSubmissionRepositoryOptions()));

        var entry = new SubmissionAuditEntry
        {
            EventId = eventId,
            SubmissionId = submissionId,
            SchemaName = "edc-site",
            SchemaVersion = 1,
            TimestampUtc = timestamp,
            Action = "UpdateSubmission",
            Outcome = new SubmissionAuditOutcome { Status = "Succeeded", HttpStatus = 200 }
        };

        // Act
        var result = await store.AppendAsync(entry, CancellationToken.None);

        // Assert
        result.ShouldSucceed();
        await workingAuditBlob.Received(1).UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>());
        processedContainer.DidNotReceive().GetBlobClient(Arg.Any<string>());
    }
}
