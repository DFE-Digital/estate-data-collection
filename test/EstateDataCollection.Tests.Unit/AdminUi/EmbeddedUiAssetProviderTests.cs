using AwesomeAssertions;
using EstateDataCollection.Admin.UI.Assets;
using Xunit;

namespace EstateDataCollection.Tests.Unit.AdminUi;

public sealed class EmbeddedUiAssetProviderTests
{
    [Theory]
    [InlineData("css/govuk-frontend.min.css", "text/css; charset=utf-8")]
    [InlineData("css/dfe-branding.css", "text/css; charset=utf-8")]
    [InlineData("js/govuk-frontend.min.js", "application/javascript; charset=utf-8")]
    [InlineData("js/workbench.js", "application/javascript; charset=utf-8")]
    [InlineData("images/favicon.ico", "image/x-icon")]
    [InlineData("images/govuk-crest.svg", "image/svg+xml")]
    [InlineData("fonts/bold-b542beb274-v2.woff2", "font/woff2")]
    public void EmbeddedUiAssetProvider_Should_Resolve_All_Assets_Including_GovUk_Asset_Subfolders(
        string path,
        string expectedContentType)
    {
        // Arrange — path supplied by theory data

        // Act
        var found = EmbeddedUiAssetProvider.TryReadAsset(path, out var bytes, out var contentType);

        // Assert
        found.Should().BeTrue();
        bytes.Length.Should().BeGreaterThan(0);
        contentType.Should().Be(expectedContentType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../secret.txt")]
    [InlineData("css/non-existent.css")]
    public void EmbeddedUiAssetProvider_Should_Reject_Missing_Or_Traversal_Paths(string path)
    {
        // Arrange — invalid path supplied by theory data

        // Act
        var found = EmbeddedUiAssetProvider.TryReadAsset(path, out var bytes, out _);

        // Assert
        found.Should().BeFalse();
        bytes.Should().BeEmpty();
    }
}
