using Azure.Storage.Blobs;
using EstateDataCollection.Application.Models;
using EstateDataCollection.Application.Models.Options;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Application.UseCases.Schema.IngestDraftSchema;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Application.UseCases.Schema.SubmitSchema;
using EstateDataCollection.Application.UseCases.Schema.VerifySchema;
using EstateDataCollection.Application.UseCases.Submissions.CreateSubmission;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.UseCases.Submissions.UpdateSubmission;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.AzureFunctions.Admin.Events;
using EstateDataCollection.AzureFunctions.Admin.Models.Dtos;
using EstateDataCollection.AzureFunctions.Admin.Routes;
using EstateDataCollection.AzureFunctions.Public.Routes;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Infrastructure.Persistence;
using EstateDataCollection.Infrastructure.Registry;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Infrastructure.Validation.Engines;
using EstateDataCollection.Tests.Integration.Extensions;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstateDataCollection.Tests.Integration.Builders;

/// <summary>
/// Fluent builder / test harness that wires Azurite-backed infrastructure, use cases,
/// and all single-responsibility Admin and Public endpoints/events for integration tests.
/// </summary>
public sealed class IntegrationEndpointHarness
{
    public BlobServiceClient BlobClient { get; }
    public BlobSchemaRegistry SchemaRegistry { get; }
    public BlobSubmissionInfrastructure SubmissionInfrastructure { get; }

    public VerifySchemaEndpoint VerifySchema { get; }
    public VerifyLogicEndpoint VerifyLogic { get; }
    public SubmitSchemaEndpoint SubmitSchema { get; }
    public ScheduleSchemaEndpoint ScheduleSchema { get; }
    public GetAdminSchemasEndpoint GetAdminSchemas { get; }
    public GetAdminSchemaVersionDetailEndpoint GetAdminSchemaVersionDetail { get; }
    public IngestDraftSchemaEvent IngestDraftSchema { get; }

    public GetSchemasEndpoint PublicGetSchemas { get; }
    public GetSchemaByNameEndpoint PublicGetSchemaByName { get; }
    public GetSchemaVersionDetailEndpoint PublicGetSchemaVersionDetail { get; }
    public CreateSubmissionEndpoint CreateSubmission { get; }
    public UpdateSubmissionEndpoint UpdateSubmission { get; }
    public FinaliseSubmissionEndpoint FinaliseSubmission { get; }

    public IntegrationEndpointHarness(AzuriteFixture fixture, Action<ValidationOptions>? configureValidation = null)
    {
        BlobClient = fixture.Client;
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var schemaOptions = Options.Create(new BlobSchemaRegistryOptions
        {
            ContainerName = "schemas",
            DraftContainerName = "draft-schemas",
            CacheKey = "schema:{schemaName}:v{version}",
            SchemaBlobDirectory = "{schemaName}/v{version}.schema.json",
            RulesBlobDirectory = "{schemaName}/v{version}.rules.json",
            OdcsBlobDirectory = "{schemaName}/v{version}.odcs.yaml",
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

        var validationSettings = new ValidationOptions
        {
            UnknownEngineBehavior = UnknownEngineBehavior.Fail,
            RequireUrnOnFinalise = true
        };
        configureValidation?.Invoke(validationSettings);
        var validationOptions = Options.Create(validationSettings);

        var logicValidator = new JsonLogicValidator(
            [new JsonLogicQualityEngine(), new CelQualityEngine()],
            validationOptions,
            new JsonLogicValidatorInstrumentation(NullLogger<JsonLogicValidator>.Instance));
        var odcsParser = new OdcsContractParser();
        var validator = new JsonSchemaNetValidator(options: validationOptions);
        var payloadValidationService = new PayloadValidationService(validator, logicValidator);

        SchemaRegistry = new BlobSchemaRegistry(
            fixture.Client,
            schemaOptions,
            memoryCache,
            logicValidator: logicValidator,
            odcsParser: odcsParser,
            validationOptions: validationOptions);
        SubmissionInfrastructure = new BlobSubmissionInfrastructure(fixture.Client, submissionOptions);

        var verifyUseCase = new VerifySchemaUseCase(SchemaRegistry, odcsParser: odcsParser);
        var submitUseCase = new SubmitSchemaUseCase(SchemaRegistry, odcsParser: odcsParser);
        var scheduleUseCase = new ScheduleSchemaUseCase(SchemaRegistry);
        var getSchemasUseCase = new GetSchemasUseCase(SchemaRegistry);
        var getSchemaDetailUseCase = new GetSchemaDetailUseCase(SchemaRegistry);
        var ingestDraftUseCase = new IngestDraftSchemaUseCase(SchemaRegistry, odcsParser: odcsParser);
        var createUseCase = new CreateSubmissionUseCase(SchemaRegistry, SubmissionInfrastructure);
        var updateUseCase = new UpdateSubmissionUseCase(SubmissionInfrastructure, SchemaRegistry, validator, validationService: payloadValidationService);
        var finaliseUseCase = new FinaliseSubmissionUseCase(SubmissionInfrastructure, SchemaRegistry, validator, validationService: payloadValidationService, validationOptions: validationOptions);

        VerifySchema = new VerifySchemaEndpoint(verifyUseCase);
        VerifyLogic = new VerifyLogicEndpoint(verifyUseCase);
        SubmitSchema = new SubmitSchemaEndpoint(submitUseCase);
        ScheduleSchema = new ScheduleSchemaEndpoint(scheduleUseCase);
        GetAdminSchemas = new GetAdminSchemasEndpoint(getSchemasUseCase);
        GetAdminSchemaVersionDetail = new GetAdminSchemaVersionDetailEndpoint(getSchemaDetailUseCase);
        IngestDraftSchema = new IngestDraftSchemaEvent(ingestDraftUseCase);

        PublicGetSchemas = new GetSchemasEndpoint(getSchemasUseCase);
        PublicGetSchemaByName = new GetSchemaByNameEndpoint(getSchemasUseCase);
        PublicGetSchemaVersionDetail = new GetSchemaVersionDetailEndpoint(getSchemaDetailUseCase);
        CreateSubmission = new CreateSubmissionEndpoint(createUseCase);
        UpdateSubmission = new UpdateSubmissionEndpoint(updateUseCase);
        FinaliseSubmission = new FinaliseSubmissionEndpoint(finaliseUseCase);
    }

    public static string UniqueName(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, 48)];

    public async Task<SubmitSchemaResult> SubmitAndActivateSchemaAsync(
        string schemaName,
        int version,
        string schemaJson,
        SchemaIdentifierConfig? identifierConfig = null)
    {
        var submitRes = await SubmitSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new SubmitSchemaBodyDto(SchemaJson: schemaJson, IdentifierConfig: identifierConfig)),
            schemaName,
            version,
            CancellationToken.None);
        var submitted = submitRes.ShouldBeApiSuccess<SubmitSchemaResult>(StatusCodes.Status201Created);

        var schedRes = await ScheduleSchema.HandleAsync(
            HttpRequestBuilder.FromJson(new ScheduleSchemaBodyDto(
                EffectiveFromUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                EffectiveToUtc: DateTimeOffset.UtcNow.AddDays(30),
                LifecycleAction: "Activate",
                IdentifierConfig: identifierConfig)),
            schemaName,
            version,
            CancellationToken.None);
        schedRes.ShouldBeApiSuccess<ScheduleSchemaResult>(StatusCodes.Status200OK);

        return submitted;
    }

    public async Task<Guid> CreateDraftSubmissionAsync(string schemaName, int? version = null)
    {
        var builder = new HttpRequestBuilder();
        if (version.HasValue)
        {
            builder.WithQuery("version", version.Value.ToString());
        }

        var res = await CreateSubmission.HandleAsync(builder.Build(), schemaName, CancellationToken.None);
        return res.ShouldBeApiSuccess<CreateSubmissionResult>(StatusCodes.Status201Created).SubmissionId;
    }
}
