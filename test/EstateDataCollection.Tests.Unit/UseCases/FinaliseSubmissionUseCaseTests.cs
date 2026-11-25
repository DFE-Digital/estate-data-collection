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
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Extensions;
using EstateDataCollection.Tests.Common.Fixtures;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class FinaliseSubmissionUseCaseTests
{
    private readonly ISubmissionInfrastructure _repository = Substitute.For<ISubmissionInfrastructure>();
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();
    private readonly ISchemaValidator _validator = Substitute.For<ISchemaValidator>();
    private readonly FinaliseSubmissionUseCase _sut;

    public FinaliseSubmissionUseCaseTests()
    {
        var validationService = new PayloadValidationService(_validator, new QualityRuleCoordinator());
        var evaluationService = new SubmissionEvaluationService(_repository, _registry, validationService);
        _sut = new FinaliseSubmissionUseCase(_repository, evaluationService);
    }

    [Fact]
    public async Task When_Finalising_An_Incomplete_Or_Invalid_Payload_It_Should_Return_Validation_Error_And_Not_Promote()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().Build();
        var resolved = new ResolvedSchemaBuilder().Build();

        _repository.GetSessionAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<SubmissionSession>.Success(session));
        _repository.GetDraftPayloadAsync(session.SubmissionId, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(SurveyPayloadFixtures.MissingUrnJson));
        _registry.GetExplicitSchemaAsync("edc_payload", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));
        _validator.ValidateAsync(SurveyPayloadFixtures.MissingUrnJson, resolved.Schema, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaValidationResult>.Success(new SchemaValidationResult(
            [
                new ValidationError("#/urn", "Required property 'urn' not found")
            ])));

        var request = new FinaliseSubmissionRequest(session.SubmissionId);

        // Act
        var result = await _sut.ExecuteAsync(request);

        // Assert
        result.ShouldFailWith("Schema.ContractViolation");
        await _repository.DidNotReceive().PromoteToProcessedAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_Finalising_A_Valid_Payload_It_Should_Promote_To_Processed_Container_And_Return_Success()
    {
        // Arrange
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

        var request = new FinaliseSubmissionRequest(session.SubmissionId);

        // Act
        var result = (await _sut.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        result.Status.Should().Be("Finalised");
        result.Identifier.Should().Be("104521");
        result.IdentifierType.Should().Be("URN");
        result.ProcessedBlobUri.Should().Contain("processed/urn=104521");
    }

    [Fact]
    public async Task Should_Extract_Nested_School_Urn_And_Enforce_Quality_Rules()
    {
        // Arrange
        var session = new SubmissionSessionBuilder().WithSchemaName("edc-school-site").Build();
        var schemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var rules = JsonSerializer.Deserialize<List<BusinessRule>>(
            EdcSchoolSiteContractBuilder.DefaultRulesJson,
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
        var validationService = new PayloadValidationService(schemaValidator, new QualityRuleCoordinator());
        var evaluationService = new SubmissionEvaluationService(_repository, _registry, validationService);
        var sut = new FinaliseSubmissionUseCase(_repository, evaluationService);
        var request = new FinaliseSubmissionRequest(session.SubmissionId);

        // Act
        var result = (await sut.ExecuteAsync(request)).ShouldSucceed();

        // Assert
        result.Identifier.Should().Be("123456");
        result.IdentifierType.Should().Be("URN");
        await _repository.Received(1).PromoteToProcessedAsync(
            session.SubmissionId, "URN", "123456", "edc-school-site", 1, Arg.Any<CancellationToken>());
    }
}
