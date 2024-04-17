using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public class CrawlerTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly RandomStringGenerator RandomStringGenerator = new (5, Alphabet.AlphaNumericLower);

    private HttpHandlerMock Http { get; } = fixture.AppHost.Services.GetRequiredService<HttpHandlerMock>();

    [Fact]
    public async Task SameLinkShouldReuseExistingMedia()
    {
        // arrange
        var url = $"https://domain.some/{RandomStringGenerator.Next()}";
        var imgUrl = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        Http.SetupImageResponse(imgUrl)
            .SetupHtmlResponse(url, h => h.Title("Title 1").Description("Description 1").Image(imgUrl));

        // act
        var sut = AppHost.Services.GetRequiredService<Crawler>();
        var meta = await sut.Crawl(url, CancellationToken.None);
        var meta2 = await sut.Crawl(url, CancellationToken.None);

        // assert
        meta.PreviewMediaId.LocalId.Should().Be("Uv2NmtPVQJIu5C079WYUgcX2OMHocEu7GA60NljCb38");
        meta.OpenGraph.Title.Should().Be("Title 1");
        meta.OpenGraph.Description.Should().Be("Description 1");
        meta2.Should().Be(meta);
    }

    [Fact]
    public async Task DifferentLinksShouldReuseExistingMedia()
    {
        // arrange
        var url1 = $"https://domain.some/{RandomStringGenerator.Next()}";
        var url2 = $"https://domain.some/{RandomStringGenerator.Next()}";
        var imgUrl = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        Http.SetupImageResponse(imgUrl)
            .SetupHtmlResponse(url1, h => h.Title("Title 1").Description("Description 1").Image(imgUrl))
            .SetupHtmlResponse(url2, h => h.Title("Title 2").Description("Description 2").Image(imgUrl));

        // act
        var sut = AppHost.Services.GetRequiredService<Crawler>();
        var meta = await sut.Crawl(url1, CancellationToken.None);
        var meta2 = await sut.Crawl(url2, CancellationToken.None);

        // assert
        meta2.PreviewMediaId.Should().Be(meta.PreviewMediaId);
        meta.PreviewMediaId.LocalId.Should().Be("Uv2NmtPVQJIu5C079WYUgcX2OMHocEu7GA60NljCb38");
        meta.OpenGraph.Title.Should().Be("Title 1");
        meta.OpenGraph.Description.Should().Be("Description 1");
        meta2.OpenGraph.Title.Should().Be("Title 2");
        meta2.OpenGraph.Description.Should().Be("Description 2");
    }
}
