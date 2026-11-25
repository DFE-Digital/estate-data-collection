using AwesomeAssertions;
using EstateDataCollection.Application.Abstraction;
using EstateDataCollection.Application.Models.Contracts;
using EstateDataCollection.Application.UseCases.Schema.DeleteSchemaVersion;
using EstateDataCollection.Application.UseCases.Schema.SignOffSchema;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Domain.Schema;
using Json.Schema;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.UseCases;

public sealed class SignOffAndDeleteSchemaUseCaseTests
{
    private readonly ISchemaRegistry _registry = Substitute.For<ISchemaRegistry>();

    [Fact]
    public async Task SignOffSchemaUseCase_Should_Transition_Unvalidated_To_Validated_And_Record_SignedOffBy()
    {
        // Arrange
        var meta = SchemaMetadata.CreateDraft("ccd-elem", 1, "{}");
        meta.MarkUnvalidated("Threshold inversion warning");

        _registry.GetExplicitSchemaAsync("ccd-elem", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(meta, JsonSchema.FromText("{}"))));
        _registry.UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new SignOffSchemaUseCase(_registry);
        var request = new SignOffSchemaRequest("ccd-elem", 1, "chris.kelly@education.gov.uk");

        // Act
        var result = await sut.ExecuteAsync(request);

        // Assert
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
        // Arrange
        var invalidMeta = SchemaMetadata.CreateInvalid("ccd-elem", 1, "{}", errorSummary: "Syntax broken");
        _registry.GetExplicitSchemaAsync("ccd-elem", 1, Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedSchema>.Success(new ResolvedSchema(invalidMeta, JsonSchema.FromText("{}"))));

        var sut = new SignOffSchemaUseCase(_registry);
        var request = new SignOffSchemaRequest("ccd-elem", 1, "reviewer");

        // Act
        var result = await sut.ExecuteAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.SignOffInvalidState");
        await _registry.DidNotReceive().UpdateSchemaMetadataAsync(Arg.Any<SchemaMetadata>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignOffSchemaUseCase_Should_Reject_Empty_SignedOffBy()
    {
        // Arrange
        var sut = new SignOffSchemaUseCase(_registry);
        var request = new SignOffSchemaRequest("ccd-elem", 1, "  ");

        // Act
        var result = await sut.ExecuteAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Schema.SignOffInvalidState");
    }

    [Fact]
    public async Task DeleteSchemaVersionUseCase_Should_Return_Deleted_Tombstone_Metadata()
    {
        // Arrange
        var deletedMeta = SchemaMetadata.CreateInvalid("ccd-elem", 1, "{}", errorSummary: "Syntax broken");
        deletedMeta.MarkDeleted();

        _registry.DeleteSchemaVersionAsync("ccd-elem", 1, Arg.Any<CancellationToken>())
            .Returns(Result<SchemaMetadata>.Success(deletedMeta));

        var sut = new DeleteSchemaVersionUseCase(_registry);
        var request = new DeleteSchemaVersionRequest("ccd-elem", 1);

        // Act
        var result = await sut.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SchemaName.Should().Be("ccd-elem");
        result.Value.Version.Should().Be(1);
        result.Value.State.Name.Should().Be("Deleted");
        result.Value.ErrorReportPreserved.Should().BeTrue();
    }
}
