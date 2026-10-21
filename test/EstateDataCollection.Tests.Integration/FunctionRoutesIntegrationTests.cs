using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.AzureFunctions.Models.Dtos;
using EstateDataCollection.AzureFunctions.Models.Results;
using EstateDataCollection.AzureFunctions.Routes;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Fixtures;
using EstateDataCollection.Tests.Integration.Fixtures;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace EstateDataCollection.Tests.Integration;

[Collection("AzuriteCollection")]
public class FunctionRoutesIntegrationTests
{
    private readonly SchemaFunctions _schemaFunctions;
    private readonly SubmissionFunctions _submissionFunctions;
    private readonly string _schemaName = "test_route_schema_" + Guid.NewGuid().ToString("N");

    public FunctionRoutesIntegrationTests(AzuriteFixture fixture)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var schemaOptions = Options.Create(new BlobSchemaRegistryOptions
        {
            ContainerName = "schemas",
            CacheKey = "schema:{schemaName}:v{version}",
            SchemaBlobDirectory = "{schemaName}/v{version}.schema.json",
            MetaBlobDirectory = "{schemaName}/v{version}.meta.json"
        });

        var submissionOptions = Options.Create(new BlobSubmissionRepositoryOptions
        {
            PayloadContainerName = "payloads",
            ProcessedContainerName = "processed",
            SessionBlobNameFormat = "{submissionId}/session.json",
            PayloadBlobNameFormat = "{submissionId}/draft.json",
            ProcessedBlobNameFormat = "urn={urn}/schema={schemaName}/v={schemaVersion}/{submissionId}.json"
        });

        var registry = new BlobSchemaRegistry(fixture.Client, schemaOptions, memoryCache);
        var infrastructure = new BlobSubmissionInfrastructure(fixture.Client, submissionOptions);
        var validator = new JsonSchemaNetValidator();

        // Build UseCases
        var verifyUseCase = new VerifySchemaUseCase(registry);
        var submitUseCase = new SubmitSchemaUseCase(registry);
        var createUseCase = new CreateSubmissionUseCase(registry, infrastructure);
        var updateUseCase = new UpdateSubmissionUseCase(infrastructure, registry, validator);
        var finaliseUseCase = new FinaliseSubmissionUseCase(infrastructure, registry, validator);

        // Instantiate Azure Functions
        _schemaFunctions = new SchemaFunctions(verifyUseCase, submitUseCase);
        _submissionFunctions = new SubmissionFunctions(createUseCase, updateUseCase, finaliseUseCase);
    }

    [Fact]
    public async Task When_Executing_End_To_End_HTTP_Route_Pipeline_From_Schema_Upload_To_Survey_Finalisation_It_Should_Succeed()
    {
        // -------------------------------------------------------------
        // Step 1: POST /schemas/verify
        // -------------------------------------------------------------
        var verifyDto = new VerifySchemaBodyDto(
            SchemaJson: JsonSchemaFixtures.ValidV1Schema,
            SampleValidFixtures: [SurveyPayloadFixtures.ValidPayloadJson],
            SampleInvalidFixtures: [SurveyPayloadFixtures.MissingUrnJson]
        );
        var verifyHttp = CreateHttpRequest(JsonSerializer.Serialize(verifyDto));

        var verifyResult = await _schemaFunctions.VerifySchema(verifyHttp, CancellationToken.None);
        var verifyObj = verifyResult.Should().BeOfType<ObjectResult>().Subject;
        verifyObj.StatusCode.Should().Be(StatusCodes.Status200OK);

        // -------------------------------------------------------------
        // Step 2: POST /schema/{schemaName}/versions/{version}
        // -------------------------------------------------------------
        var submitSchemaDto = new SubmitSchemaBodyDto(
            SchemaJson: JsonSchemaFixtures.ValidV1Schema,
            EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-5) // Active immediately
        );
        var submitSchemaHttp = CreateHttpRequest(JsonSerializer.Serialize(submitSchemaDto));

        var submitSchemaResult = await _schemaFunctions.SubmitSchema(submitSchemaHttp, _schemaName, 1, CancellationToken.None);
        var submitSchemaObj = submitSchemaResult.Should().BeOfType<ObjectResult>().Subject;
        submitSchemaObj.StatusCode.Should().Be(StatusCodes.Status201Created);

        // -------------------------------------------------------------
        // Step 3: POST /submissions/{schemeName}?version=1
        // -------------------------------------------------------------
        var createSubHttp = CreateHttpRequest(string.Empty, query: new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["version"] = "1"
        }));

        var createSubResult = await _submissionFunctions.CreateSubmission(createSubHttp, _schemaName, CancellationToken.None);
        var createSubObj = createSubResult.Should().BeOfType<ObjectResult>().Subject;
        createSubObj.StatusCode.Should().Be(StatusCodes.Status201Created);

        var apiResponse = createSubObj.Value.Should().BeOfType<ApiResponse<CreateSubmissionResult>>().Subject;
        apiResponse.Successful.Should().BeTrue();
        var submissionId = apiResponse.Data!.SubmissionId;

        // -------------------------------------------------------------
        // Step 4: PUT /submissions/{submissionId} (Partial Draft - Missing URN)
        // -------------------------------------------------------------
        var draftHttp = CreateHttpRequest(SurveyPayloadFixtures.MissingUrnJson);
        var draftResult = await _submissionFunctions.UpdateSubmission(draftHttp, submissionId, CancellationToken.None);
        var draftObj = draftResult.Should().BeOfType<ObjectResult>().Subject;
        draftObj.StatusCode.Should().Be(StatusCodes.Status200OK);

        var draftResponse = draftObj.Value.Should().BeOfType<ApiResponse<UpdateSubmissionResult>>().Subject;
        draftResponse.Successful.Should().BeTrue();
        draftResponse.Data!.IsSuccessful.Should().BeFalse(); // Incomplete, but draft saved!
        draftResponse.Data.PendingErrors.Should().NotBeEmpty();

        // -------------------------------------------------------------
        // Step 5: POST /submissions/{submissionId}/finalise (Should Fail 422 while incomplete)
        // -------------------------------------------------------------
        var prematureFinaliseHttp = CreateHttpRequest(string.Empty);
        var prematureResult = await _submissionFunctions.FinaliseSubmission(prematureFinaliseHttp, submissionId, CancellationToken.None);
        var prematureObj = prematureResult.Should().BeOfType<ObjectResult>().Subject;
        prematureObj.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);

        // -------------------------------------------------------------
        // Step 6: PUT /submissions/{submissionId} (Complete & Valid)
        // -------------------------------------------------------------
        var validDraftHttp = CreateHttpRequest(SurveyPayloadFixtures.ValidPayloadJson);
        var validDraftResult = await _submissionFunctions.UpdateSubmission(validDraftHttp, submissionId, CancellationToken.None);
        var validDraftObj = validDraftResult.Should().BeOfType<ObjectResult>().Subject;
        validDraftObj.StatusCode.Should().Be(StatusCodes.Status200OK);

        var validDraftResponse = validDraftObj.Value.Should().BeOfType<ApiResponse<UpdateSubmissionResult>>().Subject;
        validDraftResponse.Data!.IsSuccessful.Should().BeTrue();
        validDraftResponse.Data.PendingErrors.Should().BeEmpty();

        // -------------------------------------------------------------
        // Step 7: POST /submissions/{submissionId}/finalise (Success -> Promoted to processed)
        // -------------------------------------------------------------
        var finaliseHttp = CreateHttpRequest(string.Empty);
        var finaliseResult = await _submissionFunctions.FinaliseSubmission(finaliseHttp, submissionId, CancellationToken.None);
        var finaliseObj = finaliseResult.Should().BeOfType<ObjectResult>().Subject;
        finaliseObj.StatusCode.Should().Be(StatusCodes.Status200OK);

        var finaliseResponse = finaliseObj.Value.Should().BeOfType<ApiResponse<FinaliseSubmissionResult>>().Subject;
        finaliseResponse.Successful.Should().BeTrue();
        finaliseResponse.Data!.Urn.Should().Be(104521);
        finaliseResponse.Data.ProcessedBlobUri.Should().Contain("urn%3D104521");
    }

    private static HttpRequest CreateHttpRequest(string body, IQueryCollection? query = null)
    {
        var context = new DefaultHttpContext();
        if (query is not null)
        {
            context.Request.Query = query;
        }

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.Body = stream;
        context.Request.ContentLength = stream.Length;
        context.Request.ContentType = "application/json";

        return context.Request;
    }
}
