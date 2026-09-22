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
}
