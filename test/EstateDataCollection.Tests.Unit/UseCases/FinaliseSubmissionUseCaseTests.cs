using System.Text.Json;
using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Submissions;
using EstateDataCollection.Application.UseCases.Submissions.FinaliseSubmission;
using EstateDataCollection.Application.Validators;
using EstateDataCollection.Application.Validators.Logic;
using EstateDataCollection.Application.Validators.Schema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Domain.Submission;
using EstateDataCollection.Infrastructure.Validation;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class FinaliseSubmissionUseCaseTests
{
    private readonly ISubmissionInfrastructure _repository = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _validator = Substitute.For<ISchemaValidator>();
    private readonly FinaliseSubmissionUseCase _sut;

    public FinaliseSubmissionUseCaseTests()
    {
        var validationService = new PayloadValidationService(_validator, new JsonLogicValidator());
        var evaluationService = new SubmissionEvaluationService(_repository, _registry, validationService);
        _sut = new FinaliseSubmissionUseCase(_repository, evaluationService);
    }

    [Fact]
    public async Task When_Finalising_An_Incomplete_Or_Invalid_Payload_It_Should_Return_Validation_Error_And_Not_Promote()
    {
        var session = new SubmissionSessionBuilder().Build();
        var resolved = new ResolvedSchemaBuilder().Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(SurveyPayloadFixtures.MissingUrnJson));
        _registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _validator.ValidateAsync(SurveyPayloadFixtures.MissingUrnJson, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([
                new ValidationError("#/urn", "Required property 'urn' not found")
            ])));

        var result = await _sut.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId));

        result.ShouldFailWith("Schema.ContractViolation");
        await _repository.DidNotReceive().PromoteToProcessedAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Finalising_A_Valid_Payload_It_Should_Promote_To_Processed_Container_And_Return_Success()
    {
        var session = new SubmissionSessionBuilder().Build();
        var resolved = new ResolvedSchemaBuilder().Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(SurveyPayloadFixtures.ValidPayloadJson));
        _registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _validator.ValidateAsync(SurveyPayloadFixtures.ValidPayloadJson, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));
        _repository.PromoteToProcessedAsync(session.SubmissionId, "URN", "104521", "edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://storage/processed/urn=104521/final.json"));

        var result = (await _sut.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId))).ShouldSucceed();

        result.Status.Should().Be("Finalised");
        result.Identifier.Should().Be("104521");
        result.IdentifierType.Should().Be("URN");
        result.ProcessedBlobUri.Should().Contain("processed/urn=104521");
    }

    [Fact]
    public async Task When_Schema_Metadata_Specifies_Custom_Urn_JsonPointer_It_Should_Extract_Urn_From_That_Pointer()
    {
        var session = new SubmissionSessionBuilder().WithSchemaName("edc-custom-site").Build();
        const string customPayload = """{"establishment":{"identifier":139876}}""";
        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-custom-site").WithUrnPointer("/establishment/identifier"))
            .Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(customPayload));
        _registry.GetExplicitSchemaAsync("edc-custom-site", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _validator.ValidateAsync(customPayload, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));
        _repository.PromoteToProcessedAsync(session.SubmissionId, "URN", "139876", "edc-custom-site", 1, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://storage/processed/urn=139876/final.json"));

        var result = (await _sut.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId))).ShouldSucceed();

        result.Identifier.Should().Be("139876");
        result.IdentifierType.Should().Be("URN");
    }

    [Fact]
    public async Task When_Schema_Uses_Custom_String_Identifier_Should_Promote_With_Identifier_Type_And_Value()
    {
        const string laestabSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["establishment"],
          "properties": {
            "establishment": {
              "type": "object",
              "required": ["laestab"],
              "properties": { "laestab": { "type": "string" } }
            }
          }
        }
        """;

        var session = new SubmissionSessionBuilder().WithSchemaName("edc-estab").Build();
        const string payload = """{"establishment":{"laestab":"201-3614"}}""";
        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m
                .WithName("edc-estab")
                .WithSchema(laestabSchema)
                .WithIdentifierConfig(new SchemaIdentifierConfig { Type = "LAEstab", JsonPointer = "/establishment/laestab", Required = true }))
            .WithSchemaJson(laestabSchema)
            .Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(payload));
        _registry.GetExplicitSchemaAsync("edc-estab", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _validator.ValidateAsync(payload, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));
        _repository.PromoteToProcessedAsync(session.SubmissionId, "LAEstab", "201-3614", "edc-estab", 1, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success($"https://storage/processed/laestab=201-3614/schema=edc-estab/v=1/{session.SubmissionId}.json"));

        var result = (await _sut.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId))).ShouldSucceed();

        result.Identifier.Should().Be("201-3614");
        result.IdentifierType.Should().Be("LAEstab");
        result.ProcessedBlobUri.Should().Contain("laestab=201-3614");

        await _repository.Received(1).PromoteToProcessedAsync(
            session.SubmissionId, "LAEstab", "201-3614", "edc-estab", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Schema_Metadata_Marks_Identifier_As_Not_Required_It_Should_Finalise_Without_Identifier()
    {
        var session = new SubmissionSessionBuilder().WithSchemaName("edc-survey-elements").Build();
        const string elementsPayload = """{"survey":{"id":"11111111-2222-3333-4444-555555555555"},"elements":[]}""";
        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-survey-elements").WithoutUrn())
            .Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(elementsPayload));
        _registry.GetExplicitSchemaAsync("edc-survey-elements", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _validator.ValidateAsync(elementsPayload, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult([])));
        _repository.PromoteToProcessedAsync(session.SubmissionId, null, null, "edc-survey-elements", 1, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://storage/processed/schema=edc-survey-elements/v=1/final.json"));

        var result = (await _sut.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId))).ShouldSucceed();

        result.Identifier.Should().BeNull();
        result.IdentifierType.Should().BeNull();
        result.ProcessedBlobUri.Should().NotContain("urn=");
    }

    [Fact]
    public async Task Should_Extract_Nested_School_Urn_And_Enforce_Quality_Rules()
    {
        var session = new SubmissionSessionBuilder().WithSchemaName("edc-school-site").Build();
        var schemaJson = CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.schema.json");
        var rules = JsonSerializer.Deserialize<List<BusinessRule>>(
            CdeContractFixtureLoader.LoadFile("schemas/edc-school-site.rules.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-school-site").WithSchema(schemaJson))
            .WithSchemaJson(schemaJson)
            .WithRules(rules)
            .Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(CdePayloadFixtures.ValidSchoolSitePayload));
        _repository.PromoteToProcessedAsync(session.SubmissionId, "URN", "123456", "edc-school-site", 1, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://blob.local/processed/123456/edc-school-site/v1.json"));
        _registry.GetExplicitSchemaAsync("edc-school-site", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        var schemaValidator = new JsonSchemaNetValidator();
        var validationService = new PayloadValidationService(schemaValidator, new JsonLogicValidator());
        var evaluationService = new SubmissionEvaluationService(_repository, _registry, validationService);
        var sut = new FinaliseSubmissionUseCase(_repository, evaluationService);

        var result = (await sut.ExecuteAsync(new FinaliseSubmissionRequest(session.SubmissionId))).ShouldSucceed();

        result.Identifier.Should().Be("123456");
        result.IdentifierType.Should().Be("URN");
        await _repository.Received(1).PromoteToProcessedAsync(
            session.SubmissionId, "URN", "123456", "edc-school-site", 1, Arg.Any<CancellationToken>());
    }
}
