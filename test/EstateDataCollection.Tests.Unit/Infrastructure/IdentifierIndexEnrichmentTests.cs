using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Infrastructure;

public sealed class IdentifierIndexEnrichmentTests
{
    [Fact]
    public async Task UpdateSubmissionUseCase_Should_Upsert_Draft_Index_When_Identifier_Extracted()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var session = SubmissionSession.Create("edc_payload", 1);

        var infra = Substitute.For<ISubmissionInfrastructure>();
        infra.GetSessionAsync(submissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        infra.SaveDraftPayloadAsync(submissionId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        const string schemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["establishment"],
          "properties": {
            "establishment": {
              "type": "object",
              "required": ["code"],
              "properties": { "code": { "type": "string" } }
            }
          }
        }
        """;

        var resolvedSchema = new ResolvedSchemaBuilder()
            .WithSchemaJson(schemaJson)
            .ConfigureMetadata(m => m
                .WithName("edc_payload")
                .WithSchema(schemaJson)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "EstCode", JsonPointer = "/establishment/code", Required = true }))
            .Build();

        var registry = Substitute.For<ISchemaRegistry>();
        registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolvedSchema));

        var indexRepo = Substitute.For<IIdentifierIndexRepository>();
        indexRepo.UpsertEntryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IdentifierIndexEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var validationService = new PayloadValidationService(new JsonSchemaNetValidator(), new QualityRuleCoordinator());
        var evaluationService = new SubmissionEvaluationService(infra, registry, validationService);
        var sut = new UpdateSubmissionUseCase(infra, evaluationService, identifierIndexRepository: indexRepo);
        var request = new UpdateSubmissionRequest(submissionId, """{"establishment":{"code":"SCH-42"}}""");

        // Act
        var result = (await sut.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        await indexRepo.Received(1).UpsertEntryAsync(
            "EstCode",
            "SCH-42",
            Arg.Is<IdentifierIndexEntry>(e =>
                e.SubmissionId == submissionId &&
                e.SchemaName == "edc_payload" &&
                e.SchemaVersion == 1 &&
                e.Status == "Draft" &&
                e.PayloadPath == $"{submissionId}/draft.json"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteToProcessedAsync_Should_Upsert_Finalised_Index_With_BundlePrefix()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var blobServiceClient = Substitute.For<Azure.Storage.Blobs.BlobServiceClient>();
        var payloadContainer = Substitute.For<Azure.Storage.Blobs.BlobContainerClient>();
        var processedContainer = Substitute.For<Azure.Storage.Blobs.BlobContainerClient>();
        var draftBlob = Substitute.For<Azure.Storage.Blobs.BlobClient>();
        var sessionBlob = Substitute.For<Azure.Storage.Blobs.BlobClient>();
        var processedBlob = Substitute.For<Azure.Storage.Blobs.BlobClient>();

        draftBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(true, Substitute.For<Azure.Response>()));
        draftBlob.DownloadContentAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(
                Azure.Storage.Blobs.Models.BlobsModelFactory.BlobDownloadResult(BinaryData.FromString("""{"urn":104521}""")),
                Substitute.For<Azure.Response>()));
        sessionBlob.ExistsAsync(Arg.Any<CancellationToken>())
            .Returns(Azure.Response.FromValue(false, Substitute.For<Azure.Response>()));
        processedBlob.Uri.Returns(new Uri($"https://storage.local/processed/urn=104521/schema=edc/v=1/{submissionId}/payload.json"));

        blobServiceClient.GetBlobContainerClient("payloads").Returns(payloadContainer);
        blobServiceClient.GetBlobContainerClient("processed").Returns(processedContainer);
        payloadContainer.GetBlobClient($"{submissionId}/draft.json").Returns(draftBlob);
        payloadContainer.GetBlobClient($"{submissionId}/session.json").Returns(sessionBlob);
        processedContainer.GetBlobClient(Arg.Any<string>()).Returns(processedBlob);

        var indexRepo = Substitute.For<IIdentifierIndexRepository>();
        indexRepo.UpsertEntryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IdentifierIndexEntry>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var infra = new BlobSubmissionInfrastructure(
            blobServiceClient,
            Options.Create(new BlobSubmissionRepositoryOptions()),
            identifierIndexRepository: indexRepo);

        // Act
        await infra.PromoteToProcessedAsync(submissionId, "URN", "104521", "edc", 1);

        // Assert
        var expectedPrefix = $"urn=104521/schema=edc/v=1/{submissionId}/";
        await indexRepo.Received(1).UpsertEntryAsync(
            "URN",
            "104521",
            Arg.Is<IdentifierIndexEntry>(e =>
                e.SubmissionId == submissionId &&
                e.SchemaName == "edc" &&
                e.SchemaVersion == 1 &&
                e.Status == "Finalised" &&
                e.PayloadPath == $"{expectedPrefix}payload.json" &&
                e.ProcessedBundlePrefix == expectedPrefix),
            Arg.Any<CancellationToken>());
    }
}
