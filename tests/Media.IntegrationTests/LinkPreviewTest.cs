using System.Net;
using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualLab.Generators;
using Moq.Contrib.HttpClient;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public class LinkPreviewTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly RandomStringGenerator RandomStringGenerator = new (5, Alphabet.AlphaNumericLower);
    private static readonly byte[] Image = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/4QBoRXhpZgAATU0AKgAAAAgABAEaAAUAAAABAAAAPgEbAAUAAAABAAAARgEoAAMAAAABAAIAAAExAAIAAAARAAAATgAAAAAAAABgAAAAAQAAAGAAAAABcGFpbnQubmV0IDUuMC4xMgAA/9sAQwCgbniMeGSgjIKMtKqgvvD///Dc3PD//////////////////////////////////////////////////////////9sAQwGqtLTw0vD//////////////////////////////////////////////////////////////////////////////8AAEQgAEAAQAwESAAIRAQMRAf/EAB8AAAEFAQEBAQEBAAAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNicoIJChYXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk5ebn6Onq8fLz9PX29/j5+v/EAB8BAAMBAQEBAQEBAQEAAAAAAAABAgMEBQYHCAkKC//EALURAAIBAgQEAwQHBQQEAAECdwABAgMRBAUhMQYSQVEHYXETIjKBCBRCkaGxwQkjM1LwFWJy0QoWJDThJfEXGBkaJicoKSo1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoKDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uLj5OXm5+jp6vLz9PX29/j5+v/aAAwDAQACEQMRAD8AYBxknApQRjBGaoLCEUGgLH//2Q==");
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

    [Fact(Skip = "TODO: fix http client mocking for shared collection or use separate AppHost")]
    public async Task ShouldRefreshOnTextEntryChanges()
    {
        // arrange
        await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var url1 = $"https://domain.some/{RandomStringGenerator.Next()}";
        var url2 = $"https://domain.some/{RandomStringGenerator.Next()}";
        var id1 = LinkPreview.ComposeId(url1);
        var id2 = LinkPreview.ComposeId(url2);
        var img1Url = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        var img2Url = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        var http = new Mock<HttpMessageHandler>();
        http.SetupRequest(url1)
            .ReturnsResponse(HttpStatusCode.OK,
                new OpenGrapHtmlBuilder().Title("Title 1")
                    .Description("Description 1")
                    .Image(img1Url)
                    .BuildHtmlResponseContent());
        http.SetupRequest(img1Url).ReturnsResponse(HttpStatusCode.OK, new MemoryStream(Image), "image/jpeg");
        http.SetupRequest(url2)
            .ReturnsResponse(HttpStatusCode.OK,
                new OpenGrapHtmlBuilder().Title("Title 2")
                    .Description("Description 2")
                    .Image(img2Url)
                    .BuildHtmlResponseContent());
        http.SetupRequest(img2Url).ReturnsResponse(HttpStatusCode.OK, new MemoryStream(Image), "image/jpeg");
        HttpClientFactoryMock.Set(http.CreateClient());

        // act
        var entry = await Tester.CreateTextEntry(chatId, $"a b c {url1} !!!");

        // assert
        var entryLinkPreview = await GetEntryLinkPreview(entry.Id, id1);
        entryLinkPreview.Url.Should().Be(url1);
        entryLinkPreview.Title.Should().Be("Title 1");
        entryLinkPreview.Description.Should().Be("Description 1");
        entryLinkPreview.PreviewMedia.Should().NotBeNull();
        var linkPreview = await GetLinkPreview(id1);
        linkPreview.Should().BeEquivalentTo(entryLinkPreview);

        // act
        entry = await Tester.UpdateTextEntry(entry.Id, $"New text {url2} {url1}");

        // assert
        var updatedLinkPreview = await GetEntryLinkPreview(entry.Id, id2);
        updatedLinkPreview.Url.Should().Be(url2);
        updatedLinkPreview.Title.Should().Be("Title 2");
        updatedLinkPreview.Description.Should().Be("Description 2");
        updatedLinkPreview.PreviewMedia.Should().NotBeNull().And.NotBe(linkPreview.PreviewMedia);
        linkPreview = await GetLinkPreview(id2);
        linkPreview.Should().BeEquivalentTo(updatedLinkPreview);

        // act
        entry = await Tester.UpdateTextEntry(entry.Id, "Text without links");
        var nullLinkPreview = await GetEntryLinkPreview(entry.Id, Symbol.Empty);
        nullLinkPreview.Should().BeNull();
    }

    private async Task<LinkPreview> GetEntryLinkPreview(ChatEntryId entryId, Symbol expectedId)
    {
        LinkPreview? linkPreview = null;
        await TestExt.WhenMetAsync(async () => {
                var chatEntry = await Chats.GetEntry(Session, entryId).Require();
                chatEntry.LinkPreviewId.Should().Be(expectedId);
                linkPreview = chatEntry.LinkPreview;
            },
            TimeSpan.FromSeconds(5));
        return linkPreview!;
    }

    private async Task<LinkPreview> GetLinkPreview(Symbol id)
    {
        LinkPreview? linkPreview = null;
        await TestExt.WhenMetAsync(async () => {
                linkPreview = await LinkPreviews.Get(id, CancellationToken.None);
                linkPreview.Should().NotBeNull();
            },
            TimeSpan.FromSeconds(5));
        return linkPreview!;
    }
}
