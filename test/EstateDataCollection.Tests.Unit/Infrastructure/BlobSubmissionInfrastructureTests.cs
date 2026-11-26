using System.Text;
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

public class BlobSubmissionInfrastructureTests
{
    [Fact]
    public async Task PromoteToProcessedAsync_DefaultFormat_When_Identifier_Is_Unassigned_Should_Omit_Identifier_And_Target_Bundle_Payload()
    {
        // Arrange
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
            Options.Create(new BlobSubmissionRepositoryOptions()));

        // Act
        await infra.PromoteToProcessedAsync(submissionId, null, null, "edc-survey-elements", 1, CancellationToken.None);

        // Assert
        processedContainer.Received(1).GetBlobClient($"schema=edc-survey-elements/v=1/{submissionId}/payload.json");
        processedContainer.DidNotReceive().GetBlobClient($"urn=0/schema=edc-survey-elements/v=1/{submissionId}/payload.json");
    }

    [Fact]
    public async Task PromoteToProcessedAsync_With_Custom_Identifier_Should_Partition_By_Type_And_Value_Into_Bundle_Folder()
    {
        // Arrange
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

        processedBlob.Uri.Returns(new Uri($"https://storage.local/processed/laestab=201-3614/schema=edc-estab/v=1/{submissionId}/payload.json"));

        blobServiceClient.GetBlobContainerClient("payloads").Returns(payloadContainer);
        blobServiceClient.GetBlobContainerClient("processed").Returns(processedContainer);
        payloadContainer.GetBlobClient($"{submissionId}/draft.json").Returns(draftBlob);
        payloadContainer.GetBlobClient($"{submissionId}/session.json").Returns(sessionBlob);
        processedContainer.GetBlobClient(Arg.Any<string>()).Returns(processedBlob);

        var infra = new BlobSubmissionInfrastructure(
            blobServiceClient,
            Options.Create(new BlobSubmissionRepositoryOptions()));

        // Act
        var uri = (await infra.PromoteToProcessedAsync(
            submissionId,
            "LAEstab",
            "201-3614",
            "edc-estab",
            1,
            CancellationToken.None)).ShouldSucceed();

        // Assert
        uri.Should().Contain($"laestab=201-3614/schema=edc-estab/v=1/{submissionId}/payload.json");
        processedContainer.Received(1).GetBlobClient($"laestab=201-3614/schema=edc-estab/v=1/{submissionId}/payload.json");
        await processedBlob.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            Arg.Is<BlobUploadOptions>(o =>
                o.Metadata["identifier"] == "201-3614" &&
                o.Metadata["identifier_type"] == "LAEstab"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteToProcessedAsync_Should_Write_Session_Envelope_Into_Processed_Bundle_With_BundlePrefix()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var session = new SubmissionSession
        {
            SubmissionId = submissionId,
            SchemaName = "edc-school-site",
            SchemaVersion = 2
        };

        var blobServiceClient = Substitute.For<BlobServiceClient>();
        var payloadContainer = Substitute.For<BlobContainerClient>();
        var processedContainer = Substitute.For<BlobContainerClient>();
        var draftBlob = Substitute.For<BlobClient>();
        var workingSessionBlob = Substitute.For<BlobClient>();
        var processedPayloadBlob = Substitute.For<BlobClient>();
        var processedSessionBlob = Substitute.For<BlobClient>();

        var expectedPrefix = $"urn=104521/schema=edc-school-site/v=2/{submissionId}/";

        draftBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, Substitute.For<Response>()));
        draftBlob.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                BlobsModelFactory.BlobDownloadResult(BinaryData.FromString("""{"urn":104521}""")),
                Substitute.For<Response>()));

        workingSessionBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(true, Substitute.For<Response>()));
        workingSessionBlob.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(
                BlobsModelFactory.BlobDownloadResult(BinaryData.FromString(JsonSerializer.Serialize(session))),
                Substitute.For<Response>()));

        string? savedWorkingSessionJson = null;
        workingSessionBlob.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                using var reader = new StreamReader(ci.Arg<Stream>(), Encoding.UTF8, leaveOpen: true);
                savedWorkingSessionJson = reader.ReadToEnd();
                return Task.FromResult(Response.FromValue(Substitute.For<BlobContentInfo>(), Substitute.For<Response>()));
            });

        string? savedBundleSessionJson = null;
        processedSessionBlob.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                using var reader = new StreamReader(ci.Arg<Stream>(), Encoding.UTF8, leaveOpen: true);
                savedBundleSessionJson = reader.ReadToEnd();
                return Task.FromResult(Response.FromValue(Substitute.For<BlobContentInfo>(), Substitute.For<Response>()));
            });

        processedPayloadBlob.Uri.Returns(new Uri($"https://storage.local/processed/{expectedPrefix}payload.json"));

        blobServiceClient.GetBlobContainerClient("payloads").Returns(payloadContainer);
        blobServiceClient.GetBlobContainerClient("processed").Returns(processedContainer);
        payloadContainer.GetBlobClient($"{submissionId}/draft.json").Returns(draftBlob);
        payloadContainer.GetBlobClient($"{submissionId}/session.json").Returns(workingSessionBlob);
        processedContainer.GetBlobClient($"{expectedPrefix}payload.json").Returns(processedPayloadBlob);
        processedContainer.GetBlobClient($"{expectedPrefix}session.json").Returns(processedSessionBlob);

        var infra = new BlobSubmissionInfrastructure(
            blobServiceClient,
            Options.Create(new BlobSubmissionRepositoryOptions()));

        // Act
        var uri = (await infra.PromoteToProcessedAsync(
            submissionId,
            "URN",
            "104521",
            "edc-school-site",
            2,
            CancellationToken.None)).ShouldSucceed();

        // Assert
        uri.Should().EndWith($"{expectedPrefix}payload.json");
        savedWorkingSessionJson.Should().NotBeNull();
        savedBundleSessionJson.Should().NotBeNull();

        var deserialisedBundleSession = JsonSerializer.Deserialize<SubmissionSession>(savedBundleSessionJson!);
        deserialisedBundleSession!.IsFinalised.Should().BeTrue();
        deserialisedBundleSession.IdentifierType.Should().Be("URN");
        deserialisedBundleSession.IdentifierValue.Should().Be("104521");
        deserialisedBundleSession.ProcessedBundlePrefix.Should().Be(expectedPrefix);
    }

    [Fact]
    public async Task PromoteToProcessedAsync_With_Legacy_ProcessedBlobNameFormat_Should_Still_Resolve_Flat_Payload_Path()
    {
        // Arrange
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

        // Act
        await infra.PromoteToProcessedAsync(submissionId, "URN", "104521", "edc-survey-elements", 1, CancellationToken.None);

        // Assert
        processedContainer.Received(1).GetBlobClient($"urn=104521/schema=edc-survey-elements/v=1/{submissionId}.json");
    }
}
