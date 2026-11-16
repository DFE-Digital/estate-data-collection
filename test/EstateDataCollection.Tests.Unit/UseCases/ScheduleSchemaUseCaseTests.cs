using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.ScheduleSchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class ScheduleSchemaUseCaseTests
{
    [Fact]
    public async Task Should_Assign_Effective_Dates_And_IdentifierConfig_To_Unscheduled_Schema()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var existingContract = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").Unscheduled())
            .Build();

        registry.GetExplicitSchemaAsync("school-workforce", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(existingContract));
        registry.UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var useCase = new ScheduleSchemaUseCase(registry);
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddDays(30);
        var idConfig = new SchemaIdentifierConfig { Type = "URN", JsonPointer = "/school/urn", Required = true };

        var result = (await useCase.ExecuteAsync(
            new ScheduleSchemaRequest("school-workforce", 1, from, to, IdentifierConfig: idConfig),
            CancellationToken.None)).ShouldSucceed();

        result.State.Name.Should().Be(nameof(SchemaLifecycleState.Active));
        result.EffectiveFromUtc.Should().Be(from);
        result.EffectiveToUtc.Should().Be(to);
        result.IdentifierConfig!.JsonPointer.Should().Be("/school/urn");

        await registry.Received(1).UpdateSchemaMetadataAsync(
            Arg.Is<SchemaMetadata>(m =>
                m.State == SchemaLifecycleState.Active &&
                m.EffectiveFromUtc == from &&
                m.EffectiveToUtc == to &&
                m.IdentifierConfig != null &&
                m.IdentifierConfig.JsonPointer == "/school/urn"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Allow_Updating_IdentifierConfig_Independently()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var existingContract = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("edc-survey-elements").Unscheduled())
            .Build();

        registry.GetExplicitSchemaAsync("edc-survey-elements", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(existingContract));
        registry.UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var useCase = new ScheduleSchemaUseCase(registry);
        var idConfig = new SchemaIdentifierConfig { Type = "None", JsonPointer = null, Required = false };

        var result = (await useCase.ExecuteAsync(
            new ScheduleSchemaRequest("edc-survey-elements", 1, IdentifierConfig: idConfig),
            CancellationToken.None)).ShouldSucceed();

        result.State.Name.Should().Be(nameof(SchemaLifecycleState.Validated));
        result.IdentifierConfig!.Type.Should().Be("None");
        result.IdentifierConfig.Required.Should().BeFalse();
    }
}
