using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class BotAuthorTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private IAuthorsBackend AuthorsBackend => field ??= AppHost.Services.GetRequiredService<IAuthorsBackend>();

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Alice.SignInAsAlice();
    }

    protected override async Task DisposeAsync()
    {
        await Alice.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task BotAuthorsShouldGetDescendingNegativeLocalIds()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Bots" });
        var bot1 = await NewBot("bot-one");
        var bot2 = await NewBot("bot-two");

        // act
        var author1 = await Commander.Call(new AuthorsBackend_Upsert(chatId, null, bot1, null, new AuthorDiff()));
        var author2 = await Commander.Call(new AuthorsBackend_Upsert(chatId, null, bot2, null, new AuthorDiff()));
        var again = await Commander.Call(new AuthorsBackend_Upsert(chatId, null, bot1, null, new AuthorDiff()));

        // assert
        author1.Id.LocalId.Should().Be(-3, "the first bot sits right below Sherlock");
        author2.Id.LocalId.Should().Be(-4);
        again.Id.Should().Be(author1.Id, "upsert is idempotent per user");
        Bots.IsBot(author1.Id).Should().BeTrue();
        (await AuthorsBackend.ListAuthorIds(chatId, default)).Should().Contain(author1.Id);
    }

    private async Task<UserId> NewBot(string name)
    {
        var userId = UserId.Parse("whintest" + Guid.NewGuid().ToString("N")[..10]);
        await InternalAccounts.Create(AppHost.Services, new InternalUserInfo(userId, name, AvatarName: name) { IsBot = true }, default);
        return userId;
    }
}
