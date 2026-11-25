using AwesomeAssertions;
using EstateDataCollection.AzureFunctions.Admin.Routes.Admin;
using EstateDataCollection.Tests.Integration.Builders;
using EstateDataCollection.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EstateDataCollection.Tests.Integration.AdminUi;

[Collection("AzuriteCollection")]
public sealed class StaticAssetEndpointIntegrationTests(AzuriteFixture fixture)
    : IDisposable
{
    private readonly IntegrationEndpointHarness<GetStaticAssetEndpoint> _harness = new(fixture);
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Handle_CssAsset_Should_Return_TextCss_With_Content()
    {
        // Arrange
        var request = HttpRequestBuilder.Empty();

        // Act
        var result = _harness.Endpoint.Handle(request, "css/dfe-branding.css");

        // Assert
        var cssFile = result.Should().BeOfType<FileContentResult>().Subject;
        cssFile.ContentType.Should().Be("text/css; charset=utf-8");
        cssFile.FileContents.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Handle_JsAsset_Should_Return_JavaScript_With_CacheControl_And_ETag()
    {
        // Arrange
        var request = HttpRequestBuilder.Empty();

        // Act
        var result = _harness.Endpoint.Handle(request, "js/workbench.js");

        // Assert
        var jsFile = result.Should().BeOfType<FileContentResult>().Subject;
        jsFile.ContentType.Should().Be("application/javascript; charset=utf-8");
        jsFile.FileContents.Length.Should().BeGreaterThan(0);
        request.HttpContext.Response.Headers.CacheControl.ToString()
            .Should().Be("public, max-age=3600, must-revalidate");

        var etag = request.HttpContext.Response.Headers.ETag.ToString();
        etag.Should().NotBeNullOrWhiteSpace();
        etag.Should().StartWith("\"").And.EndWith("\"");
    }

    [Fact]
    public void Handle_WithMatchingIfNoneMatchETag_Should_Return_304_NotModified()
    {
        // Arrange
        var initialRequest = HttpRequestBuilder.Empty();
        _harness.Endpoint.Handle(initialRequest, "js/workbench.js");
        var etag = initialRequest.HttpContext.Response.Headers.ETag.ToString();

        var cachedRequest = new HttpRequestBuilder()
            .WithHeader("If-None-Match", etag)
            .Build();

        // Act
        var result = _harness.Endpoint.Handle(cachedRequest, "js/workbench.js");

        // Assert
        result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void Handle_CustomCacheDuration_Should_Emit_Configured_MaxAge_Header()
    {
        // Arrange
        var customSpanEndpoint = new GetStaticAssetEndpoint(cacheDuration: TimeSpan.FromMinutes(15));
        var request = HttpRequestBuilder.Empty();

        // Act
        var result = customSpanEndpoint.Handle(request, "images/govuk-crest.svg");

        // Assert
        result.Should().BeOfType<FileContentResult>()
            .Which.ContentType.Should().Be("image/svg+xml");
        request.HttpContext.Response.Headers.CacheControl.ToString()
            .Should().Be("public, max-age=900, must-revalidate");
    }

    [Fact]
    public void Handle_MissingAsset_Should_Return_404_NotFound()
    {
        // Arrange
        var request = HttpRequestBuilder.Empty();

        // Act
        var result = _harness.Endpoint.Handle(request, "css/does-not-exist.css");

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }
}
