using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Builders.Contracts;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class ScheduleSchemaUseCaseTests
{
    private const string OptionalUkprnSchema = """
    {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "required": ["survey"],
      "properties": {
        "establishment": {
          "type": "object",
          "properties": {
            "ukprn": { "type": "string" }
          }
        },
        "survey": { "type": "string" }
      }
    }
    """;

    [Fact]
    public async Task Should_Assign_Effective_Dates_And_Verified_IdentifierConfig_To_Unscheduled_Schema()
    {
        // Arrange
        var registry = Substitute.For<ISchemaRegistry>();
        var nestedSchemaJson = EdcSchoolSiteContractBuilder.DefaultSchemaJson;
        var existingContract = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").WithSchema(nestedSchemaJson).Unscheduled())
            .WithSchemaJson(nestedSchemaJson)
            .Build();

        registry.GetExplicitSchemaAsync("school-workforce", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(existingContract));
        registry.UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new ScheduleSchemaUseCase(registry);
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddDays(30);
        var idConfig = new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = null };
        var request = new ScheduleSchemaRequest("school-workforce", 1, from, to, IdentifierConfig: idConfig);

        // Act
        var result = (await sut.ExecuteAsync(request, CancellationToken.None)).ShouldSucceed();

        // Assert
        result.State.Name.Should().Be(nameof(SchemaLifecycleState.Active));
        result.EffectiveFromUtc.Should().Be(from);
        result.EffectiveToUtc.Should().Be(to);
        result.IdentifierConfig!.JsonPointer.Should().Be("/school/urn");
        result.IdentifierConfig.Required.Should().BeTrue();

        await registry.Received(1).UpdateSchemaMetadataAsync(
            Arg.Is<SchemaMetadata>(m =>
                m.State == SchemaLifecycleState.Active &&
                m.EffectiveFromUtc == from &&
                m.EffectiveToUtc == to &&
                m.IdentifierConfig != null &&
                m.IdentifierConfig.JsonPointer == "/school/urn" &&
                m.IdentifierConfig.Required == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_IdentifierPointer_Does_Not_Exist_In_Schema_Should_Fail_And_Not_Save()
    {
        // Arrange
        var registry = Substitute.For<ISchemaRegistry>();
        var existingContract = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").Unscheduled())
            .Build();

        registry.GetExplicitSchemaAsync("school-workforce", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(existingContract));

        var sut = new ScheduleSchemaUseCase(registry);
        var invalidConfig = new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn" };
        var request = new ScheduleSchemaRequest("school-workforce", 1, IdentifierConfig: invalidConfig);

        // Act
        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        // Assert
        result.ShouldFailWith("Schema.InvalidIdentifierConfig");
        await registry.DidNotReceive().UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Derive_Required_From_Schema_Required_Array_Even_If_Caller_Passes_Override()
    {
        // Arrange
        var registry = Substitute.For<ISchemaRegistry>();
        var existingContract = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("optional-ukprn").WithSchema(OptionalUkprnSchema).Unscheduled())
            .WithSchemaJson(OptionalUkprnSchema)
            .Build();

        registry.GetExplicitSchemaAsync("optional-ukprn", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(existingContract));
        registry.UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new ScheduleSchemaUseCase(registry);
        var idConfig = new SchemaIdentifierConfig
        {
            Type = "UKPRN",
            JsonPointer = "/establishment/ukprn",
            Required = true
        };
        var request = new ScheduleSchemaRequest("optional-ukprn", 1, IdentifierConfig: idConfig);

        // Act
        var result = (await sut.ExecuteAsync(request, CancellationToken.None)).ShouldSucceed();

        // Assert
        result.IdentifierConfig!.Type.Should().Be("UKPRN");
        result.IdentifierConfig.JsonPointer.Should().Be("/establishment/ukprn");
        result.IdentifierConfig.Required.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Allow_Updating_IdentifierConfig_To_None_Independently()
    {
        // Arrange
        var registry = Substitute.For<ISchemaRegistry>();
        var existingContract = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-survey-elements").Unscheduled())
            .Build();

        registry.GetExplicitSchemaAsync("edc-survey-elements", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(existingContract));
        registry.UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new ScheduleSchemaUseCase(registry);
        var idConfig = new SchemaIdentifierConfig { Type = "None", JsonPointer = null, Required = false };
        var request = new ScheduleSchemaRequest("edc-survey-elements", 1, IdentifierConfig: idConfig);

        // Act
        var result = (await sut.ExecuteAsync(request, CancellationToken.None)).ShouldSucceed();

        // Assert
        result.State.Name.Should().Be(nameof(SchemaLifecycleState.Validated));
        result.IdentifierConfig!.Type.Should().Be("None");
        result.IdentifierConfig.Required.Should().BeFalse();
    }
}
