using System.Text;
using AwesomeAssertions;
using EstateDataCollection.Application.UseCases;
using EstateDataCollection.Common;
using EstateDataCollection.Common.Results;
using EstateDataCollection.Common.Web.Endpoints;
using EstateDataCollection.Common.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace EstateDataCollection.Tests.Unit.Common;

public class HttpRequestSecurityAndResultTests
{
    [Fact]
    public async Task ReadRequestBodyAsync_When_Chunked_Stream_Exceeds_Configured_Limit_Should_Throw_RequestBodyTooLargeException()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items[HttpRequestExtensions.MaxBodySizeItemKey] = 32L;
        // Omit Content-Length intentionally to simulate chunked transfer encoding
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 64)));

        // Act
        var act = async () => await context.Request.ReadRequestBodyAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<RequestBodyTooLargeException>();
    }

    [Fact]
    public async Task ReadRequestBodyAsync_When_Chunked_Stream_Is_Within_Limit_Should_Return_Body()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items[HttpRequestExtensions.MaxBodySizeItemKey] = 64L;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"ok\":true}"));

        // Act
        var body = await context.Request.ReadRequestBodyAsync(CancellationToken.None);

        // Assert
        body.Should().Be("{\"ok\":true}");
    }

    [Fact]
    public async Task FunctionEndpointBase_When_RequestBodyTooLargeException_Thrown_Should_Return_413_ProblemDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items[HttpRequestExtensions.MaxBodySizeItemKey] = 16L;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 32)));
        var endpoint = new TestEndpoint();
        var useCase = Substitute.For<IUseCase<string, string>>();

        // Act
        var result = await endpoint.InvokeWithBodyLimitAsync(context.Request, useCase);

        // Assert
        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public void ResultExtensions_When_ErrorType_Is_Unexpected_Should_Map_To_500_InternalServerError()
    {
        // Arrange
        var error = Error.Unexpected("Storage.Failure", "Boom");

        // Act
        var actionResult = error.ToActionResult();

        // Assert
        var obj = actionResult.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void AppVersionProvider_Keys_Should_Use_OpenTelemetry_Semantic_Names()
    {
        // Arrange — static constants on AppVersionProvider.Keys

        // Act
        var versionKey = AppVersionProvider.Keys.Version;
        var shaKey = AppVersionProvider.Keys.Sha;

        // Assert
        versionKey.Should().Be("service.version");
        shaKey.Should().Be("service.sha");
    }

    private sealed class TestEndpoint : FunctionEndpointBase
    {
        public Task<IActionResult> InvokeWithBodyLimitAsync(HttpRequest request, IUseCase<string, string> useCase)
        {
            return ExecuteAsync(
                "TestEndpoint",
                useCase,
                async () =>
                {
                    var body = await request.ReadRequestBodyAsync(CancellationToken.None);
                    return Result<string>.Success(body);
                });
        }
    }
}
