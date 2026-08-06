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
        var url1 = $"https://domain1.some/{RandomStringGenerator.Next()}";
        var url2 = $"https://domain1.some/{RandomStringGenerator.Next()}";
        var id1 = LinkPreview.ComposeId(url1);
        var id2 = LinkPreview.ComposeId(url2);
        var img1Url = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        var img2Url = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        Http.SetupImage(img1Url)
            .SetupHtml(url1, h => h.Title("Title 1").Description("Description 1").Image(img1Url))
            .SetupImage(img2Url)
            .SetupHtml(url2, h => h.Title("Title 2").Description("Description 2").Image(img2Url))
            .SetupEmptyRobots(url1)
            .SetupEmptyRobots(url2);

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
        var linkPreview1 = await ComputedTest.When(ct => Previews.Get(id1, ct).Require());
        linkPreview1.Should().BeEquivalentTo(entryLinkPreview);

        // act
        entry = await Tester.UpdateTextEntry(entry.Id, $"New text {url2} {url1}");

        // assert
        var updatedEntryLinkPreviews = await GetEntryLinkPreviews(entry.Id, id2, id1)!.Require();
        updatedEntryLinkPreviews[0].Url.Should().Be(url2);
        updatedEntryLinkPreviews[0].Title.Should().Be("Title 2");
        updatedEntryLinkPreviews[0].Description.Should().Be("Description 2");
        updatedEntryLinkPreviews[0].PreviewMedia.Should().NotBeNull().And.NotBe(linkPreview1.PreviewMedia);
        var linkPreview2 = await ComputedTest.When(ct => Previews.Get(id2, ct).Require());
        linkPreview2.Should().BeEquivalentTo(updatedEntryLinkPreviews[0]);
        updatedEntryLinkPreviews[1].Url.Should().Be(url1);
        updatedEntryLinkPreviews[1].Title.Should().Be("Title 1");
        updatedEntryLinkPreviews[1].Description.Should().Be("Description 1");
        updatedEntryLinkPreviews[1].PreviewMedia.Should().NotBeNull().And.Be(linkPreview1.PreviewMedia);
        linkPreview1 = await ComputedTest.When(ct => Previews.Get(id1, ct).Require());
        linkPreview1.Should().BeEquivalentTo(updatedEntryLinkPreviews[1]);

        // act
        entry = await Tester.UpdateTextEntry(entry.Id, "Text without links");
        var nullEntryLinkPreview = await GetEntryLinkPreviews(entry.Id);
        nullEntryLinkPreview.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(11)]
    [InlineData(20)]
    [InlineData(100)]
    public async Task ShouldCreateManyLinkPreviews(int count)
    {
        // arrange
        var urls = Enumerable.Range(1, count).Select(i => $"https://domain{i}.some/{RandomStringGenerator.Next()}").ToList();
        var ids = urls.Select(LinkPreview.ComposeId).ToList();
        var imgUrls = Enumerable.Range(1, count).Select(i => $"https://domain{i}.some/{RandomStringGenerator.Next()}.jpg").ToList();

        for (int i = 0; i < count; i++) {
            var url = urls[i];
            var imgUrl = imgUrls[i];
            var suffix = (i + 1).ToString();
            Http.SetupImage(imgUrl)
                .SetupHtml(url, h => h.Title($"Title {suffix}").Description($"Description {suffix}").Image(imgUrl))
                .SetupEmptyRobots(url);
        }

        // act
        await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var sLinks = string.Join(" !!! ", urls);
        var entry = await Tester.CreateTextEntry(chatId, $"a b c {sLinks} d e f");

        // assert
        var expectedIds = ids.Take(Constants.Media.LinkPreviewsPerMessageLimit).ToList();
        var entryLinkPreviews = await GetEntryLinkPreviews(entry.Id, expectedIds);
        for (int i = 0; i < expectedIds.Count; i++) {
            var id = ids[i];
            var url = urls[i];
            var entryLinkPreview = entryLinkPreviews[i];
            entryLinkPreview.Url.Should().Be(url);
            entryLinkPreview.Title.Should().Be($"Title {i + 1}");
            entryLinkPreview.Description.Should().Be($"Description {i + 1}");
            entryLinkPreview.PreviewMedia.Should().NotBeNull();

            var linkPreview = await ComputedTest.When(ct => Previews.Get(id, ct).Require());
            linkPreview.Should().BeEquivalentTo(entryLinkPreview);
        }
    }

    [Fact]
    public async Task ShouldSkipEmails()
    {
        // arrange
        var url1 = $"https://domain1.some/{RandomStringGenerator.Next()}";
        var id1 = LinkPreview.ComposeId(url1);
        var img1Url = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        Http.SetupImage(img1Url)
            .SetupHtml(url1, h => h.Title("Title 1").Description("Description 1").Image(img1Url))
            .SetupEmptyRobots(url1);

        // act
        await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, $"a b c alice{Constants.Team.EmailSuffix} {url1} 123");

        // assert
        var entryLinkPreview = await GetEntryLinkPreview(entry.Id, id1).Require();
        entryLinkPreview.Url.Should().Be(url1);
        entryLinkPreview.Title.Should().Be("Title 1");
        entryLinkPreview.Description.Should().Be("Description 1");
        entryLinkPreview.PreviewMedia.Should().NotBeNull();
        var linkPreview = await ComputedTest.When(ct => Previews.Get(id1, ct).Require());
        linkPreview.Should().BeEquivalentTo(entryLinkPreview);
    }

    [Fact]
    public async Task ShouldSkipInvalidSchemes()
    {
        // arrange
        var url1 = $"https://domain1.some/{RandomStringGenerator.Next()}";
        var id1 = LinkPreview.ComposeId(url1);
        var img1Url = $"https://domain2.some/images/{RandomStringGenerator.Next()}.jpg";
        Http.SetupImage(img1Url)
            .SetupHtml(url1, h => h.Title("Title 1").Description("Description 1").Image(img1Url))
            .SetupEmptyRobots(url1);

        // act
        await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, $"a b c mailto://alice{Constants.Team.EmailSuffix} ftp://domaine.some/123 app://domain.some/123  {url1} 123");

        // assert
        var entryLinkPreview = await GetEntryLinkPreview(entry.Id, id1).Require();
        entryLinkPreview.Url.Should().Be(url1);
        entryLinkPreview.Title.Should().Be("Title 1");
        entryLinkPreview.Description.Should().Be("Description 1");
        entryLinkPreview.PreviewMedia.Should().NotBeNull();
        var linkPreview = await ComputedTest.When(ct => Previews.Get(id1, ct).Require());
        linkPreview.Should().BeEquivalentTo(entryLinkPreview);
    }

    private async Task<LinkPreview?> GetEntryLinkPreview(
        ChatEntryId entryId,
        Symbol expectedId)
    {
        var linkPreviews = await GetEntryLinkPreviews(entryId, expectedId);
        return linkPreviews.FirstOrDefault();
    }

    private async Task<LinkPreview[]> GetEntryLinkPreviews(ChatEntryId entryId, params IReadOnlyList<Symbol> expectedIds)
        => await ComputedTest.When(async ct => {
            var chatEntry = await Chats.GetEntry(Session, entryId, ct).Require();
            chatEntry.LinkPreviewIds.Should().BeEquivalentTo(expectedIds);
            chatEntry.LinkPreviews.Select(x => x.Id).Should().BeEquivalentTo(expectedIds);
            return chatEntry.LinkPreviews;
        }, TimeSpan.FromSeconds(10));
}
