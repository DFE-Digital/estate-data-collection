using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.GetSubmissionStatus;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Admin.Events;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes.Api;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fuzzing;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EstateDataCollection.Tests.Integration.EndToEnd;

[Collection("AzuriteCollection")]
public sealed class BogusNegativePipelineIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    public static IEnumerable<object[]> AllSchemaProfiles()
    {
        foreach (var profile in Enum.GetValues<FuzzSchemaProfile>())
        {
            yield return [profile];
        }
    }

    public static IEnumerable<object[]> PublicHttpNegativeCases()
    {
        // Exercise representative negative mutation categories against every schema contract end-to-end in Azurite
        yield return [FuzzSchemaProfile.ValidV1, NegativeMutationKind.MissingRequiredRootProperty, 11];
        yield return [FuzzSchemaProfile.ValidV1, NegativeMutationKind.WrongTypeStringForNumericOrBool, 12];
        yield return [FuzzSchemaProfile.ValidV1, NegativeMutationKind.AdditionalPropertyDisallowed, 13];
        yield return [FuzzSchemaProfile.ValidV1, NegativeMutationKind.AdversarialStringInTypedField, 14];
        yield return [FuzzSchemaProfile.ValidV1, NegativeMutationKind.NonObjectJsonRoot, 15];

        yield return [FuzzSchemaProfile.EdcSchoolSite, NegativeMutationKind.MissingRequiredNestedProperty, 21];
        yield return [FuzzSchemaProfile.EdcSchoolSite, NegativeMutationKind.PatternViolation, 22];
        yield return [FuzzSchemaProfile.EdcSchoolSite, NegativeMutationKind.InvalidEmailFormat, 23];
        yield return [FuzzSchemaProfile.EdcSchoolSite, NegativeMutationKind.InvalidDateTimeFormat, 24];
        yield return [FuzzSchemaProfile.EdcSchoolSite, NegativeMutationKind.JsonLogicQualityRuleViolation, 25];
        yield return [FuzzSchemaProfile.EdcSchoolSite, NegativeMutationKind.MissingOrBlankIdentifier, 26];

        yield return [FuzzSchemaProfile.EdcSurveyElements, NegativeMutationKind.WrongTypeObjectForArray, 31];
        yield return [FuzzSchemaProfile.EdcSurveyElements, NegativeMutationKind.PatternViolation, 32];
        yield return [FuzzSchemaProfile.EdcSurveyElements, NegativeMutationKind.InvalidEnumValue, 33];
        yield return [FuzzSchemaProfile.EdcSurveyElements, NegativeMutationKind.JsonLogicQualityRuleViolation, 34];

        yield return [FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers, NegativeMutationKind.NullInNonNullableField, 41];
        yield return [FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers, NegativeMutationKind.InvalidEnumValue, 42];
        yield return [FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers, NegativeMutationKind.AdversarialStringInTypedField, 43];
        yield return [FuzzSchemaProfile.EdcSurveyEvidenceAndAnswers, NegativeMutationKind.JsonLogicQualityRuleViolation, 44];

        yield return [FuzzSchemaProfile.CustomLaEstabCel, NegativeMutationKind.PatternViolation, 51];
        yield return [FuzzSchemaProfile.CustomLaEstabCel, NegativeMutationKind.AdditionalPropertyDisallowed, 52];
        yield return [FuzzSchemaProfile.CustomLaEstabCel, NegativeMutationKind.JsonLogicQualityRuleViolation, 53];
        yield return [FuzzSchemaProfile.CustomLaEstabCel, NegativeMutationKind.CelQualityRuleViolation, 54];
        yield return [FuzzSchemaProfile.CustomLaEstabCel, NegativeMutationKind.MissingOrBlankIdentifier, 55];
    }

    public static IEnumerable<object[]> MalformedAndEmptyBodyCases()
    {
        yield return [FuzzSchemaProfile.ValidV1, string.Empty, StatusCodes.Status400BadRequest];
        yield return [FuzzSchemaProfile.ValidV1, "   \t\r\n  ", StatusCodes.Status400BadRequest];
        yield return [FuzzSchemaProfile.ValidV1, "null", StatusCodes.Status422UnprocessableEntity];
        yield return [FuzzSchemaProfile.EdcSchoolSite, BogusNegativePayloadSuite.GenerateNegativeCase(FuzzSchemaProfile.EdcSchoolSite, NegativeMutationKind.MalformedJsonSyntax, 77).PayloadJson, StatusCodes.Status422UnprocessableEntity];
        yield return [FuzzSchemaProfile.EdcSurveyElements, BogusNegativePayloadSuite.GenerateNegativeCase(FuzzSchemaProfile.EdcSurveyElements, NegativeMutationKind.MalformedJsonSyntax, 88).PayloadJson, StatusCodes.Status422UnprocessableEntity];
        yield return [FuzzSchemaProfile.CustomLaEstabCel, BogusNegativePayloadSuite.GenerateNegativeCase(FuzzSchemaProfile.CustomLaEstabCel, NegativeMutationKind.MalformedJsonSyntax, 99).PayloadJson, StatusCodes.Status422UnprocessableEntity];
    }

    [Theory]
    [MemberData(nameof(AllSchemaProfiles))]
    public async Task Bogus_Valid_Baselines_Across_All_Schemas_Should_Pass_Full_Http_Pipeline(FuzzSchemaProfile profile)
    {
        var contract = BogusNegativePayloadSuite.GetContract(profile);
        var schemaName = IntegrationEndpointHarness.UniqueName(contract.SchemaNamePrefix);
        var validPayload = BogusNegativePayloadSuite.GenerateValidJson(profile, 701);

        var verifyDto = new VerifySchemaBodyDto(
            SchemaJson: contract.SchemaJson,
            RulesJson: contract.RulesJson,
            SampleValidFixtures: [validPayload],
            IdentifierConfig: contract.IdentifierConfig);
        (await _harness.Get<PostVerifySchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(verifyDto), CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status200OK);

        await _harness.SubmitAndActivateSchemaAsync(
            schemaName,
            1,
            contract.SchemaJson,
            contract.IdentifierConfig,
            contract.RulesJson);

        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);

        var update = (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(validPayload), subId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        update.IsSuccessful.Should().BeTrue($"valid Bogus baseline for {profile} must succeed");
        update.PendingErrors.Should().BeEmpty();

        var status = (await _harness.Get<GetSubmissionStatusEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        status.IsReadyToFinalise.Should().BeTrue();

        var finalise = (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<FinaliseSubmissionResult>(StatusCodes.Status200OK);
        finalise.Status.Should().Be("Finalised");
        finalise.IdentifierType.Should().Be(contract.IdentifierConfig.Type);
        finalise.Identifier.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(PublicHttpNegativeCases))]
    public async Task Bogus_Negative_Payloads_Across_Schemas_Should_Fail_Along_Public_Http_And_Audit_Pipeline(
        FuzzSchemaProfile profile,
        NegativeMutationKind mutationKind,
        int seed)
    {
        var contract = BogusNegativePayloadSuite.GetContract(profile);
        var schemaName = IntegrationEndpointHarness.UniqueName($"{contract.SchemaNamePrefix}_neg");

        await _harness.SubmitAndActivateSchemaAsync(
            schemaName,
            1,
            contract.SchemaJson,
            contract.IdentifierConfig,
            contract.RulesJson);

        var negativeCase = BogusNegativePayloadSuite.GenerateNegativeCase(profile, mutationKind, seed);
        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);

        // 1. PUT negative Bogus payload -> 200 OK with IsSuccessful = false and PendingErrors
        var update = (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(negativeCase.PayloadJson), subId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        update.IsSuccessful.Should().BeFalse($"negative case {negativeCase.Description} must return pending errors");
        update.PendingErrors.Should().NotBeEmpty();

        // 2. GET /status -> IsReadyToFinalise = false
        var status = (await _harness.Get<GetSubmissionStatusEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        status.IsReadyToFinalise.Should().BeFalse();
        status.PendingErrors.Should().NotBeEmpty();

        // 3. POST /finalise -> 422 UnprocessableEntity
        (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);

        // 4. Verify nothing promoted into processed container for this submission
        var processedContainer = _harness.BlobClient.GetBlobContainerClient(_harness.ProcessedContainerName);
        if (await processedContainer.ExistsAsync())
        {
            await foreach (var blob in processedContainer.GetBlobsAsync())
            {
                blob.Name.Should().NotContain(subId.ToString());
            }
        }

        // 5. Verify audit trail recorded every step cleanly without crashing on adversarial strings
        var auditEntries = (await _harness.Get<ISubmissionAuditStore>().ListAsync(subId)).ShouldSucceed();
        auditEntries.Should().HaveCount(4);
        auditEntries.Select(e => e.Action).Should().Equal(
            "CreateSubmission",
            "UpdateSubmission",
            "CheckSubmissionStatus",
            "FinaliseSubmission");
        auditEntries[1].Outcome.Status.Should().Be("SavedWithPendingErrors");
        auditEntries[3].Outcome.Status.Should().Be("Rejected");
        auditEntries[3].Outcome.HttpStatus.Should().Be(422);
    }

    [Theory]
    [MemberData(nameof(MalformedAndEmptyBodyCases))]
    public async Task Bogus_Malformed_And_Empty_Payloads_On_PutSubmission_Should_Reject_Cleanly_Without_500(
        FuzzSchemaProfile profile,
        string rawBody,
        int expectedPutStatus)
    {
        var contract = BogusNegativePayloadSuite.GetContract(profile);
        var schemaName = IntegrationEndpointHarness.UniqueName($"{contract.SchemaNamePrefix}_mal");

        await _harness.SubmitAndActivateSchemaAsync(
            schemaName,
            1,
            contract.SchemaJson,
            contract.IdentifierConfig,
            contract.RulesJson);

        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);

        var putResult = await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
            HttpRequestBuilder.FromRaw(rawBody),
            subId,
            CancellationToken.None);
        putResult.ShouldHaveStatusCode(expectedPutStatus);

        // No draft chunk saved -> status reports awaiting payload & finalise rejects cleanly (400 BadRequest)
        var status = (await _harness.Get<GetSubmissionStatusEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldBeApiSuccess<GetSubmissionStatusResult>(StatusCodes.Status200OK);
        status.IsReadyToFinalise.Should().BeFalse();

        (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Bogus_Negative_Merge_Patch_On_Second_Put_Should_Poison_Draft_And_Block_Finalisation_In_Azurite()
    {
        var contract = BogusNegativePayloadSuite.GetContract(FuzzSchemaProfile.CustomLaEstabCel);
        var schemaName = IntegrationEndpointHarness.UniqueName("bogus_patch_poison");

        await _harness.SubmitAndActivateSchemaAsync(
            schemaName,
            1,
            contract.SchemaJson,
            contract.IdentifierConfig,
            contract.RulesJson);

        var subId = await _harness.CreateDraftSubmissionAsync(schemaName, 1);
        var validBaseline = BogusNegativePayloadSuite.GenerateValidJson(FuzzSchemaProfile.CustomLaEstabCel, 555);

        // Chunk 1: valid draft
        var chunk1 = (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(validBaseline), subId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        chunk1.IsSuccessful.Should().BeTrue();

        // Chunk 2: Bogus patch introducing CEL rule violation (open_issues > total_checks)
        var celViolationPatch = """{"summary":{"total_checks":4,"open_issues":99}}""";
        var chunk2 = (await _harness.Get<PutUpdateSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.FromRaw(celViolationPatch), subId, CancellationToken.None))
            .ShouldBeApiSuccess<UpdateSubmissionResult>(StatusCodes.Status200OK);
        chunk2.IsSuccessful.Should().BeFalse();
        chunk2.DraftCount.Should().Be(2);
        chunk2.PendingErrors.Should().Contain(e => e.RuleId == "CEL-SUITE-ISSUES-1");

        // Finalise blocked with 422
        (await _harness.Get<PostFinaliseSubmissionEndpoint>().HandleAsync(
                HttpRequestBuilder.Empty(), subId, CancellationToken.None))
            .ShouldHaveStatusCode(StatusCodes.Status422UnprocessableEntity);
    }

    [Theory]
    [MemberData(nameof(AllSchemaProfiles))]
    public async Task Bogus_Negative_Fixtures_Should_Reject_Admin_Verify_And_Draft_Blob_Ingestion(
        FuzzSchemaProfile profile)
    {
        var contract = BogusNegativePayloadSuite.GetContract(profile);
        var validFixture = BogusNegativePayloadSuite.GenerateValidJson(profile, 901);
        var negativeFixture = BogusNegativePayloadSuite.GenerateNegativeCase(
            profile,
            NegativeMutationKind.WrongTypeStringForNumericOrBool,
            902).PayloadJson;

        // 1. Admin /api/schemas/verify returns 200 OK diagnostic report with IsSuccessful = false for negative fixture
        var verifyDto = new VerifySchemaBodyDto(
            SchemaJson: contract.SchemaJson,
            RulesJson: contract.RulesJson,
            SampleValidFixtures: [negativeFixture],
            IdentifierConfig: contract.IdentifierConfig);
        var verifyReport = (await _harness.Get<PostVerifySchemaEndpoint>().HandleAsync(
                HttpRequestBuilder.FromJson(verifyDto), CancellationToken.None))
            .ShouldBeApiSuccess<EstateDataCollection.Application.UseCases.Schema.VerifySchema.VerifySchemaResult>(StatusCodes.Status200OK);
        verifyReport.IsSuccessful.Should().BeFalse();
        verifyReport.FixtureErrors.Should().NotBeEmpty();

        // 2. Draft blob ingestion consumes failing draft into schemas container as Invalid with v1.error.json
        var draftSchemaName = IntegrationEndpointHarness.UniqueName($"{contract.SchemaNamePrefix}_ingest_neg");
        var draftContainer = _harness.BlobClient.GetBlobContainerClient(_harness.DraftSchemasContainerName);
        var schemasContainer = _harness.BlobClient.GetBlobContainerClient(_harness.SchemasContainerName);
        await draftContainer.CreateIfNotExistsAsync();

        var schemaPath = $"{draftSchemaName}/v1.schema.json";
        var validFixturePath = $"{draftSchemaName}/v1.valid.json";
        var metaPath = $"{draftSchemaName}/v1.meta.json";

        await draftContainer.GetBlobClient(schemaPath).UploadAsync(BinaryData.FromString(contract.SchemaJson), overwrite: true);
        if (!string.IsNullOrWhiteSpace(contract.RulesJson))
        {
            await draftContainer.GetBlobClient($"{draftSchemaName}/v1.rules.json")
                .UploadAsync(BinaryData.FromString(contract.RulesJson), overwrite: true);
        }
        await draftContainer.GetBlobClient(validFixturePath)
            .UploadAsync(BinaryData.FromString(JsonSerializer.Serialize(new[] { JsonDocument.Parse(validFixture).RootElement, JsonDocument.Parse(negativeFixture).RootElement })), overwrite: true);
        await draftContainer.GetBlobClient(metaPath)
            .UploadAsync(BinaryData.FromString(JsonSerializer.Serialize(new { identifierConfig = contract.IdentifierConfig })), overwrite: true);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contract.SchemaJson));
        var ingestResult = (await _harness.Get<IngestDraftSchemaEvent>().HandleAsync(stream, schemaPath, CancellationToken.None)).ShouldSucceed();

        ingestResult.Promoted.Should().BeFalse();
        ingestResult.Status.Should().Be("Invalid");
        ingestResult.HasErrorReport.Should().BeTrue();
        (await schemasContainer.GetBlobClient($"{draftSchemaName}/v1.error.json").ExistsAsync()).Value.Should().BeTrue();
        (await _harness.Get<ISchemaRegistry>().ExistsAsync(draftSchemaName, 1)).ShouldSucceed().Should().BeTrue();
    }
}
