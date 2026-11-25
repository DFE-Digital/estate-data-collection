using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.DeleteSchemaVersion;
using EstateDataCollection.Application.UseCases.Schema.SignOffSchema;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using NSubstitute;
using Json.Schema;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class SignOffAndDeleteSchemaUseCaseTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();

    [Fact]
    public async Task SignOffSchemaUseCase_Should_Transition_Unvalidated_To_Validated_And_Record_SignedOffBy()
    {
        var meta = SchemaMetadata.CreateDraft("ccd-elem", 1, "{}");
        meta.MarkUnvalidated("Threshold inversion warning");

        _registry.GetExplicitSchemaAsync("ccd-elem", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(meta, JsonSchema.FromText("{}"))));
        _registry.UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new SignOffSchemaUseCase(_registry);

        var result = await sut.ExecuteAsync(new SignOffSchemaRequest("ccd-elem", 1, "chris.kelly@education.gov.uk"));

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Name.Should().Be("Validated");
        result.Value.SignedOffBy.Should().Be("chris.kelly@education.gov.uk");
        meta.State.Should().Be(SchemaLifecycleState.Validated);
        meta.SignedOffBy.Should().Be("chris.kelly@education.gov.uk");
        meta.SignedOffAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task SignOffSchemaUseCase_Should_Reject_Invalid_Or_Validated_Schemas()
    {
        var invalidMeta = SchemaMetadata.CreateInvalid("ccd-elem", 1, "{}", errorSummary: "Syntax broken");
        _registry.GetExplicitSchemaAsync("ccd-elem", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(invalidMeta, JsonSchema.FromText("{}"))));

        var sut = new SignOffSchemaUseCase(_registry);

        var result = await sut.ExecuteAsync(new SignOffSchemaRequest("ccd-elem", 1, "reviewer"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.SignOffInvalidState");
        await _registry.DidNotReceive().UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignOffSchemaUseCase_Should_Reject_Empty_SignedOffBy()
    {
        var sut = new SignOffSchemaUseCase(_registry);

        var result = await sut.ExecuteAsync(new SignOffSchemaRequest("ccd-elem", 1, "  "));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.SignOffInvalidState");
    }

    [Fact]
    public async Task DeleteSchemaVersionUseCase_Should_Return_Deleted_Tombstone_Metadata()
    {
        var deletedMeta = SchemaMetadata.CreateInvalid("ccd-elem", 1, "{}", errorSummary: "Syntax broken");
        deletedMeta.MarkDeleted();

        _registry.DeleteSchemaVersionAsync("ccd-elem", 1, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaMetadata>.Success(deletedMeta));

        var sut = new DeleteSchemaVersionUseCase(_registry);

        var result = await sut.ExecuteAsync(new DeleteSchemaVersionRequest("ccd-elem", 1));

        result.IsSuccess.Should().BeTrue();
        result.Value.SchemaName.Should().Be("ccd-elem");
        result.Value.Version.Should().Be(1);
        result.Value.State.Name.Should().Be("Deleted");
        result.Value.ErrorReportPreserved.Should().BeTrue();
    }
}
