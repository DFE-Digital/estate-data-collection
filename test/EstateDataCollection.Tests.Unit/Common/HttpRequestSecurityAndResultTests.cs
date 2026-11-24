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
        var context = new DefaultHttpContext();
        context.Items[HttpRequestExtensions.MaxBodySizeItemKey] = 32L;
        // Omit Content-Length intentionally to simulate chunked transfer encoding
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 64)));

        var act = async () => await context.Request.ReadRequestBodyAsync(CancellationToken.None);

        await act.Should().ThrowAsync<RequestBodyTooLargeException>();
    }

    [Fact]
    public async Task ReadRequestBodyAsync_When_Chunked_Stream_Is_Within_Limit_Should_Return_Body()
    {
        var context = new DefaultHttpContext();
        context.Items[HttpRequestExtensions.MaxBodySizeItemKey] = 64L;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"ok\":true}"));

        var body = await context.Request.ReadRequestBodyAsync(CancellationToken.None);

        body.Should().Be("{\"ok\":true}");
    }

    [Fact]
    public async Task FunctionEndpointBase_When_RequestBodyTooLargeException_Thrown_Should_Return_413_ProblemDetails()
    {
        var context = new DefaultHttpContext();
        context.Items[HttpRequestExtensions.MaxBodySizeItemKey] = 16L;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 32)));

        var endpoint = new TestEndpoint();
        var useCase = Substitute.For<IUseCase<string, string>>();

        var result = await endpoint.InvokeWithBodyLimitAsync(context.Request, useCase);

        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    [Fact]
    public void ResultExtensions_When_ErrorType_Is_Unexpected_Should_Map_To_500_InternalServerError()
    {
        var actionResult = Error.Unexpected("Storage.Failure", "Boom").ToActionResult();

        var obj = actionResult.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void AppVersionProvider_Keys_Should_Use_OpenTelemetry_Semantic_Names()
    {
        AppVersionProvider.Keys.Version.Should().Be("service.version");
        AppVersionProvider.Keys.Sha.Should().Be("service.sha");
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
