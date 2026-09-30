using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class BotAccountTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IAccountsBackend AccountsBackend => field ??= AppHost.Services.GetRequiredService<IAccountsBackend>();

    [Fact]
    public async Task InternalCreateShouldProduceBotAccount()
    {
        // arrange
        var userId = UserId.Parse("whinbottest" + Guid.NewGuid().ToString("N")[..8]);
        var info = new InternalUserInfo(userId, "alerts-bot", AvatarName: "Alerts") { IsBot = true };

        // act
        var account = await InternalAccounts.Create(AppHost.Services, info, CancellationToken.None);
        var reloaded = await AccountsBackend.Get(userId, CancellationToken.None);

        // assert
        account.IsBot.Should().BeTrue();
        reloaded!.IsBot.Should().BeTrue();
        reloaded.Avatar.Name.Should().Be("Alerts");
        reloaded.Status.Should().Be(AccountStatus.Active);
    }

    [Fact]
    public async Task RepeatedCreateShouldKeepTheSameBotIdentity()
    {
        // arrange
        var userId = UserId.Parse("whinbotagain" + Guid.NewGuid().ToString("N")[..8]);
        var info = new InternalUserInfo(userId, "alerts-bot", AvatarName: "Alerts") { IsBot = true };
        var first = await InternalAccounts.Create(AppHost.Services, info, CancellationToken.None);

        // act
        var second = await InternalAccounts.Create(AppHost.Services, info, CancellationToken.None);

        // assert
        second.Avatar.Id.Should().Be(first.Avatar.Id, "get-or-create must not orphan the previous avatar");
        second.Avatar.Bio.Should().BeEmpty("a bot gets no stand-in test-account bio");
        second.Avatar.PictureUrl.Should().BeEmpty("a bot gets no generated picture");
    }

    [Fact]
    public async Task GreeterShouldNotKeepCyclingOverBots()
    {
        // arrange
        var userId = UserId.New();
        var info = new InternalUserInfo(userId, "bot") { IsBot = true };
        var bot = await InternalAccounts.Create(AppHost.Services, info, CancellationToken.None);
        if (bot.IsGreetingCompleted) {
            // The host's own greeter may have reached the account before it became a bot
            var reset = new AccountsBackend_Update(bot with { IsGreetingCompleted = false }, bot.Version);
            await AppHost.Services.Commander().Call(reset, true, CancellationToken.None);
        }
        bot = await AccountsBackend.Get(userId, CancellationToken.None).Require();
        bot.IsGreetingCompleted.Should().BeFalse("the test needs a bot the old query would pick up");
        var greeter = new TestContactGreeter(AppHost.Services);

        // act, assert
        // Polled: a pass reports whether it found anyone, and accounts other tests left are greeted pass by pass
        await TestWait.WhenPolled(async ()
            => (await greeter.RunOnce(CancellationToken.None))
                .Should().BeTrue("a bot is never greeted, so it must not count as pending"));
    }

    [Fact]
    public async Task BotShouldNotSignIn()
    {
        // arrange
        var userId = UserId.Parse("whinbotsign" + Guid.NewGuid().ToString("N")[..8]);
        await InternalAccounts.Create(AppHost.Services, new InternalUserInfo(userId, "bot") { IsBot = true }, CancellationToken.None);
        var identity = new UserIdentity(UserIdentity.InternalSchema, userId.Value);
        var command = new AccountsBackend_SignIn(Session.New(), identity, new ApiMap<UserIdentity, string>(), new ApiMap<string, string>(), AutoCreate: false);

        // act
        var error = await Record.ExceptionAsync(() => AppHost.Services.Commander().Call(command, true, CancellationToken.None));

        // assert
        error.Should().NotBeNull("a bot account has no way to sign in");
    }

    [Fact]
    public async Task BotShouldBeOffline()
    {
        // arrange
        var userId = UserId.Parse("whinbotpres" + Guid.NewGuid().ToString("N")[..8]);
        await InternalAccounts.Create(AppHost.Services, new InternalUserInfo(userId, "bot") { IsBot = true }, CancellationToken.None);
        var presences = AppHost.Services.GetRequiredService<IUserPresences>();

        // act
        var presence = await presences.Get(userId, CancellationToken.None);

        // assert
        presence.Should().Be(Presence.Offline);
    }

    // Nested types

    private sealed class TestContactGreeter(IServiceProvider services) : ContactGreeter(services)
    {
        public Task<bool> RunOnce(CancellationToken cancellationToken)
            => OnActivate(cancellationToken);
    }
}
