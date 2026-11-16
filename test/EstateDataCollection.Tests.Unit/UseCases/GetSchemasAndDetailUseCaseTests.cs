using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.GetSchemaDetail;
using EstateDataCollection.Application.UseCases.Schema.GetSchemas;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using EstateDataCollection.Tests.Common.Builders;
using EstateDataCollection.Tests.Common.Extensions;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public class GetSchemasAndDetailUseCaseTests
{
    [Fact]
    public async Task GetSchemasUseCase_Should_Exclude_Unscheduled_Schemas_When_IncludeUnscheduled_Is_False()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var unscheduledMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(1).Unscheduled().Build();
        var activeMeta = new SchemaMetadataBuilder().WithName("school-workforce").WithVersion(2).Build();

        registry.ListSchemasAsync(null, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<SchemaMetadata>>.Success([unscheduledMeta, activeMeta]));

        var useCase = new GetSchemasUseCase(registry);

        var publicResult = (await useCase.ExecuteAsync(new GetSchemasRequest(IncludeUnscheduled: false), CancellationToken.None)).ShouldSucceed();
        publicResult.Schemas.Should().ContainSingle();
        publicResult.Schemas[0].Versions.Should().ContainSingle(v => v.Version == 2);

        var adminResult = (await useCase.ExecuteAsync(new GetSchemasRequest(IncludeUnscheduled: true), CancellationToken.None)).ShouldSucceed();
        adminResult.Schemas[0].Versions.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSchemaDetailUseCase_Should_Return_NotFound_When_Schema_Is_Unscheduled_And_IncludeUnscheduled_Is_False()
    {
        var registry = Substitute.For<ISchemaRegistry>();
        var resolved = new ResolvedSchemaBuilder()
            .ConfigureMetadata(m => m.WithName("school-workforce").Unscheduled())
            .Build();

        registry.GetExplicitSchemaAsync("school-workforce", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(resolved));

        var useCase = new GetSchemaDetailUseCase(registry);

        var publicResult = await useCase.ExecuteAsync(
            new GetSchemaDetailRequest("school-workforce", 1, IncludeUnscheduled: false),
            CancellationToken.None);
        publicResult.IsFailure.Should().BeTrue();
        publicResult.Error.Type.Should().Be(ErrorType.NotFound);

        var adminResult = (await useCase.ExecuteAsync(
            new GetSchemaDetailRequest("school-workforce", 1, IncludeUnscheduled: true),
            CancellationToken.None)).ShouldSucceed();
        adminResult.State.Name.Should().Be("Validated");
        adminResult.EffectiveFromUtc.Should().BeNull();
    }
}
