using System.Net;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Media.IntegrationTests;

[Collection(nameof(MediaCollection))]
public sealed class GithubLinkPreviewTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly RandomStringGenerator RandomStringGenerator = new (5, Alphabet.AlphaNumericLower);
    private IChats Chats { get; } = fixture.AppHost.Services.GetRequiredService<IChats>();
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private HttpHandlerMock Http { get; } = fixture.AppHost.Services.GetRequiredService<HttpHandlerMock>();
    private Session Session => Tester.Session;

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task PullRequestLinkShouldGetItsCard()
    {
        // arrange
        var repo = $"acme/repo-{RandomStringGenerator.Next()}";
        var url = $"https://github.com/{repo}/pull/5168";
        var id = LinkPreview.ComposeId(url);
        Http.SetupJson($"https://api.github.com/repos/{repo}/pulls/5168",
            """
            {
              "number": 5168,
              "title": "Fix the coach tip banner",
              "state": "open",
              "user": { "login": "iqmulator" },
              "created_at": "2026-10-08T07:12:30Z",
              "base": { "ref": "dev" },
              "head": { "ref": "fix/coach-tip-banner" },
              "additions": 50,
              "deletions": 9,
              "changed_files": 6
            }
            """);

        // act
        await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, $"please review {url}");

        // assert
        var preview = await GetEntryLinkPreview(entry.Id, id).Require();
        preview.Title.Should().Be("Fix the coach tip banner");
        preview.Description.Should().BeEmpty();
        preview.PreviewMedia.Should().BeNull("the card is drawn from the API record, not the Open Graph image");
        var card = preview.GithubCard;
        card.Should().NotBeNull();
        card.Kind.Should().Be(GithubLinkKind.PullRequest);
        card.Repo.Should().Be(repo);
        card.Number.Should().Be(5168);
        card.Author.Should().Be("iqmulator");
        card.CreatedAt.Should().Be(Moment.Parse("2026-10-08T07:12:30Z"));
        card.BaseRef.Should().Be("dev");
        card.HeadRef.Should().Be("fix/coach-tip-banner");
        card.Additions.Should().Be(50);
        card.Deletions.Should().Be(9);
        card.ChangedFiles.Should().Be(6);
    }

    [Fact]
    public async Task IssueLinkShouldGetItsCard()
    {
        // arrange
        var repo = $"acme/repo-{RandomStringGenerator.Next()}";
        var url = $"https://github.com/{repo}/issues/4898#issuecomment-1";
        var id = LinkPreview.ComposeId(url);
        Http.SetupJson($"https://api.github.com/repos/{repo}/issues/4898",
            """
            {
              "number": 4898,
              "title": "GitHub card images are fetched anonymously",
              "user": { "login": "frolyo" },
              "created_at": "2026-09-28T10:00:00Z",
              "type": { "name": "Bug" },
              "assignees": [ { "login": "frolyo" }, { "login": "iqmulator" } ],
              "labels": [ { "name": "media" } ]
            }
            """);

        // act
        await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, url);

        // assert
        var preview = await GetEntryLinkPreview(entry.Id, id).Require();
        preview.Title.Should().Be("GitHub card images are fetched anonymously");
        var card = preview.GithubCard;
        card.Should().NotBeNull();
        card.Kind.Should().Be(GithubLinkKind.Issue);
        card.Number.Should().Be(4898);
        card.IssueType.Should().Be("Bug");
        card.Assignees.Should().Equal("frolyo", "iqmulator");
        card.Labels.Should().Equal("media");
    }

    [Fact]
    public async Task UnreadableRepositoryLinkShouldFallBackToTheCrawl()
    {
        // arrange
        var repo = $"acme/repo-{RandomStringGenerator.Next()}";
        var url = $"https://github.com/{repo}/pull/1";
        var id = LinkPreview.ComposeId(url);
        Http.SetupStatus($"https://api.github.com/repos/{repo}/pulls/1", HttpStatusCode.NotFound)
            .SetupHtml(url, h => h.Title("Title 1").Description("Description 1"))
            .SetupEmptyRobots(url);

        // act
        await Tester.SignInAsAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.CreateTextEntry(chatId, url);

        // assert
        var preview = await GetEntryLinkPreview(entry.Id, id).Require();
        preview.Title.Should().Be("Title 1");
        preview.Description.Should().Be("Description 1");
        preview.GithubCard.Should().BeNull();
    }

    // Private methods

    private async Task<LinkPreview?> GetEntryLinkPreview(ChatEntryId entryId, Symbol expectedId)
        => await TestWait.When(async ct => {
            var chatEntry = await Chats.GetEntry(Session, entryId, ct).Require();
            chatEntry.LinkPreviews.Select(x => x.Id).Should().BeEquivalentTo([expectedId]);
            return chatEntry.LinkPreviews[0];
        }, TimeSpan.FromSeconds(10));
}
