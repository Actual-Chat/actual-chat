using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class AuthorsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Anonymous => field ??= fixture.AppHost.NewWebClientTester(Out);

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Alice.SignInAsAlice();
    }

    protected override async Task DisposeAsync()
    {
        await Alice.DisposeSilentlyAsync();
        await Anonymous.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact(Skip = "Fails on CI")]
    public async Task NullAuthorResult()
    {
        var startedAt = CpuTimestamp.Now;
        var appHost = AppHost;
        await using var tester = appHost.NewWebClientTester(Out);
        WriteLine($"{startedAt}: app host init");
        var session = tester.Session;

        var authors = tester.ClientServices.GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, Constants.Chat.DefaultChatId, default);
        WriteLine($"{startedAt}: get author");
        author.Should().BeNull();
    }

    [Fact]
    public async Task ShouldNotFailForAnonymousAuthors()
    {
        // arrange
        var (chatId, inviteId) = await Alice.CreateChat(x => x with {
            IsPublic = true,
            AllowAnonymousAuthors = true,
            AllowGuestAuthors = true,
        });
        var (anonymousAuthorId, _) = await Anonymous.JoinChat(chatId, inviteId, true);

        // act
        var anonymousAuthor = await Anonymous.GetOwnAuthor(chatId).Require();
        var anonymousAuthorForAlice = await Alice.GetAuthor(anonymousAuthorId).Require();

        // assert
        anonymousAuthorForAlice.Should().BeEquivalentTo(anonymousAuthor.ToAuthor());
    }
}
