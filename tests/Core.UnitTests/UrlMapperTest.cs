namespace ActualChat.Core.UnitTests;

public class UrlMapperTest
{
    [Theory]
    [InlineData("https://voxt.ai/", "https://maps.voxt.ai/")]
    [InlineData("https://dev.voxt.ai/", "https://maps.dev.voxt.ai/")]
    [InlineData("https://local.voxt.ai/", "https://maps.local.voxt.ai/")]
    [InlineData("https://local.actual.chat/", "https://maps.local.actual.chat/")]
    [InlineData("https://wt1.local.voxt.ai/", "https://maps-wt1.local.voxt.ai/")]
    public void MapTilesBaseUrlIsBuiltForVoxtHosts(string baseUrl, string expected)
    {
        var mapper = new UrlMapper(baseUrl);
        mapper.MapTilesBaseUrl.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://actual.chat/")]
    [InlineData("https://localhost:7080/")]
    [InlineData("https://example.com/")]
    public void MapTilesBaseUrlIsEmptyForNonVoxtHosts(string baseUrl)
    {
        var mapper = new UrlMapper(baseUrl);
        mapper.MapTilesBaseUrl.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://voxt.ai/")]
    [InlineData("https://local.voxt.ai/")]
    [InlineData("https://wt1.local.voxt.ai/")]
    public void MapTilesBaseUrlUsesSameSubdomainSchemeAsImageProxy(string baseUrl)
    {
        var mapper = new UrlMapper(baseUrl);
        // maps.* must share the scheme/host/separator of media.* so nginx routes it identically.
        mapper.MapTilesBaseUrl.Should().Be(mapper.ImageProxyBaseUrl.Replace("media", "maps"));
    }

    [Fact]
    public void MapViewStyleUrlPointsAtOwnDomain()
    {
        var mapper = new UrlMapper("https://local.voxt.ai/");
        var styleUrl = $"{mapper.MapTilesBaseUrl}styles/liberty";
        styleUrl.Should().Be("https://maps.local.voxt.ai/styles/liberty");
    }

    [Theory]
    [InlineData("media/abc/photo.heic")]
    [InlineData("media/abc/photo.HEIF")]
    public void ImageOriginalUrlShouldRouteHeifThroughProxyPassthrough(string contentId)
    {
        // arrange
        var mapper = new UrlMapper("https://dev.voxt.ai/");
        var url = mapper.ContentUrl(contentId);

        // act
        var originalUrl = mapper.ImageOriginalUrl(url);

        // assert
        originalUrl.Should().Be($"https://media.dev.voxt.ai/0/https://cdn.dev.voxt.ai/{contentId}");
    }

    [Theory]
    [InlineData("https://dev.voxt.ai/", "media/abc/photo.jpg")]
    [InlineData("https://dev.voxt.ai/", "media/abc/anim.gif")]
    [InlineData("https://localhost:7080/", "media/abc/photo.heic")]
    public void ImageOriginalUrlShouldKeepUrlWhenNoConversionIsNeeded(string baseUrl, string contentId)
    {
        // arrange
        var mapper = new UrlMapper(baseUrl);
        var url = mapper.ContentUrl(contentId);

        // act
        var originalUrl = mapper.ImageOriginalUrl(url);

        // assert
        originalUrl.Should().Be(url, "only a HEIF behind an image proxy needs the passthrough URL");
    }
}
