using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public class LinkPreviewTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly RandomStringGenerator RandomStringGenerator = new (5, Alphabet.AlphaNumericLower);
    private IChats Chats { get; } = fixture.AppHost.Services.GetRequiredService<IChats>();
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private HttpHandlerMock Http { get; } = fixture.AppHost.Services.GetRequiredService<HttpHandlerMock>();
    private IMediaLinkPreviews Previews { get; } = fixture.AppHost.Services.GetRequiredService<IMediaLinkPreviews>();
    private Session Session => Tester.Session;

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldRefreshOnTextEntryChanges()
    {
        // arrange
        var url1 = $"https://domain.some/{RandomStringGenerator.Next()}";
        var url2 = $"https://domain.some/{RandomStringGenerator.Next()}";
        var id1 = LinkPreview.ComposeId(url1);
        var id2 = LinkPreview.ComposeId(url2);
        var img1Url = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        var img2Url = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        Http.SetupImageResponse(img1Url)
            .SetupHtmlResponse(url1, h => h.Title("Title 1").Description("Description 1").Image(img1Url))
            .SetupImageResponse(img2Url)
            .SetupHtmlResponse(url2, h => h.Title("Title 2").Description("Description 2").Image(img2Url));

        // act
        await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, $"a b c {url1} !!!");

        // assert
        var entryLinkPreview = await GetEntryLinkPreview(entry.Id, id1).Require();
        entryLinkPreview.Url.Should().Be(url1);
        entryLinkPreview.Title.Should().Be("Title 1");
        entryLinkPreview.Description.Should().Be("Description 1");
        entryLinkPreview.PreviewMedia.Should().NotBeNull();
        var linkPreview = await ComputedTestExt.When(AppHost.Services, ct => Previews.Get(id1, ct).Require());
        linkPreview.Should().BeEquivalentTo(entryLinkPreview);

        // act
        entry = await Tester.UpdateTextEntry(entry.Id, $"New text {url2} {url1}");

        // assert
        var updatedEntryLinkPreview = await GetEntryLinkPreview(entry.Id, id2).Require();
        updatedEntryLinkPreview.Url.Should().Be(url2);
        updatedEntryLinkPreview.Title.Should().Be("Title 2");
        updatedEntryLinkPreview.Description.Should().Be("Description 2");
        updatedEntryLinkPreview.PreviewMedia.Should().NotBeNull().And.NotBe(linkPreview.PreviewMedia);
        linkPreview = await ComputedTestExt.When(AppHost.Services, ct => Previews.Get(id2, ct).Require());
        linkPreview.Should().BeEquivalentTo(updatedEntryLinkPreview);

        // act
        entry = await Tester.UpdateTextEntry(entry.Id, "Text without links");
        var nullEntryLinkPreview = await GetEntryLinkPreview(entry.Id, Symbol.Empty);
        nullEntryLinkPreview.Should().BeNull();
    }

    private async Task<LinkPreview?> GetEntryLinkPreview(ChatEntryId entryId, Symbol expectedId)
        => await ComputedTestExt.When(AppHost.Services,
            async ct => {
                var chatEntry = await Chats.GetEntry(Session, entryId, ct).Require();
                chatEntry.LinkPreviewId.Should().Be(expectedId);
                return expectedId.IsEmpty ? chatEntry.LinkPreview.RequireNull() : chatEntry.LinkPreview.Require();
            },
            TimeSpan.FromSeconds(10));
}
