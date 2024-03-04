using System.Net;
using System.Text;
using ActualChat.Chat;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;
using ActualLab.Generators;
using Moq.Contrib.HttpClient;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public class LinkPreviewTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly RandomStringGenerator RandomStringGenerator = new (5, Alphabet.AlphaNumericLower);
    private IMediaLinkPreviews LinkPreviews { get; } = fixture.AppHost.Services.GetRequiredService<IMediaLinkPreviews>();
    private IChats Chats { get; } = fixture.AppHost.Services.GetRequiredService<IChats>();
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private HttpClientFactoryMock HttpClientFactoryMock { get; } = fixture.AppHost.Services.GetRequiredService<HttpClientFactoryMock>();
    private Session Session => Tester.Session;

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldRefreshTextEntryCreation()
    {
        // arrange
        var url = $"https://domain.some/{RandomStringGenerator.Next()}";
        var imgUrl = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var http = new Mock<HttpMessageHandler>();
        http.SetupRequest(url)
            .ReturnsResponse(HttpStatusCode.OK,
                new StringContent($"""
                                   html prefix="og: https://ogp.me/ns#">
                                   <head>
                                   <title>The Rock (1996)</title>
                                   <meta property="og:title" content="The Rock" />
                                   <meta property="og:type" content="video.movie" />
                                   <meta property="og:url" content="https://www.imdb.com/title/tt0117500/" />
                                   <meta property="og:image" content="{imgUrl}" />
                                   </head>
                                   <body></body>
                                   </html>
                                   """, Encoding.UTF8, "text/html"));
        http.SetupRequest(imgUrl).ReturnsResponse(HttpStatusCode.OK, new MemoryStream([0, 1, 2]), "image/jpeg");
        HttpClientFactoryMock.Set(http.CreateClient());

        // act
        var entry = await Tester.CreateTextEntry(chatId, $"a b c {url} !!!");

        // assert
        var linkPreview = await GetEntryLinkPreview(entry.Id);
        linkPreview.Url.Should().Be(url);
        linkPreview.Title.Should().Be("The Rock");
        linkPreview.PreviewMedia.Should().NotBeNull();
    }

    private async Task<LinkPreview> GetEntryLinkPreview(ChatEntryId entryId)
    {
        LinkPreview? linkPreview = null;
        await TestExt.WhenMetAsync(async () => {
                var chatEntry = await Chats.GetEntry(Session, entryId).Require();
                chatEntry.LinkPreview.Should().NotBeNull();
                linkPreview = chatEntry.LinkPreview;
            },
            TimeSpan.FromSeconds(5));
        return linkPreview!;
    }
}
