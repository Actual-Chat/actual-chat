using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

// A null Value for a singleton settings key means the client sent a StoredSettings union tag
// this server doesn't know (an unknown tag deserializes to null, not an exception) - treating
// it as a delete silently wipes the user's settings. Parameterized "@"-prefixed keys keep
// null-as-delete (e.g. ChatUserSettings removal).

[Collection(nameof(UserCollection))]
public class UserSettingsNullSetTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task NullValueForSingletonSettingsKeyIsRejected()
    {
        // arrange
        await Tester.SignInAsBob();
        var command = new UserSettings_Set { Session = Tester.Session, Key = nameof(UserPttSettings), Value = null };

        // act
        var act = () => Tester.Commander.Call(command);

        // assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task NullValueForParameterizedKeyStillDeletes()
    {
        // arrange
        await Tester.SignInAsBob();
        var chatId = ChatId.Parse("testchatid1234567890");
        var key = ChatUserSettings.GetKey(chatId);
        await Tester.Commander.Call(
            new UserSettings_Set {
                Session = Tester.Session,
                Key = key,
                Value = new ChatUserSettings { VoiceMode = VoiceMode.JustVoice },
            });

        // act
        await Tester.Commander.Call(new UserSettings_Set { Session = Tester.Session, Key = key, Value = null });

        // assert
        var settings = AppHost.Services.GetRequiredService<IUserSettings>();
        var value = await settings.Get(Tester.Session, key);
        value.Should().BeNull();
    }

    [Fact]
    public async Task HiddenKeyCannotBeWrittenByClient()
    {
        // arrange
        await Tester.SignInAsBob();
        var chatId = ChatId.Parse("testchatid1234567890");
        var key = ChatInviteSettings.GetKey(chatId);

        // act
        var setSettings = () => Tester.Commander.Call(
            new UserSettings_Set {
                Session = Tester.Session,
                Key = key,
                Value = new ChatInviteSettings { InviteId = "someInviteId" },
            });
        var setKvas = () => Tester.Commander.Call(new ServerKvas_Set {
            Session = Tester.Session,
            Key = key,
            Value = [1, 2, 3],
        });

        // assert
        await setSettings.Should().ThrowAsync<Exception>();
        await setKvas.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GuestKeyMigrationLeavesHiddenKeysBehind()
    {
        // arrange
        await Tester.SignInAsBob();
        var account = await Tester.Accounts.GetOwn(Tester.Session, default);
        var sessionInfo = await Tester.Accounts.GetSessionInfo(Tester.Session, default);
        var guestId = sessionInfo.GetGuestId().Require();
        var chatId = ChatId.Parse("testchatid1234567890");
        var hiddenKey = ChatInviteSettings.GetKey(chatId);
        var plainKey = ChatUserSettings.GetKey(chatId);
        var kvasBackend = AppHost.Services.GetRequiredService<IServerKvasBackend>();
        var guestKvas = kvasBackend.ForUser(guestId);
        await guestKvas.Set<ChatInviteSettings>(hiddenKey, new ChatInviteSettings { InviteId = "someInviteId" });
        await guestKvas.Set<ChatUserSettings>(plainKey, new ChatUserSettings { VoiceMode = VoiceMode.JustVoice });

        // act
        await Tester.Commander.Call(new ServerKvas_MigrateGuestKeys { Session = Tester.Session });

        // assert
        var userKvas = kvasBackend.ForUser(account.Id);
        var migratedHidden = await userKvas.Get<ChatInviteSettings>(hiddenKey);
        var migratedPlain = await userKvas.Get<ChatUserSettings>(plainKey);
        migratedHidden.Should().BeNull();
        migratedPlain.Should().NotBeNull();
    }

    [Fact]
    public async Task HiddenKeyIsInvisibleToClient()
    {
        // arrange
        await Tester.SignInAsBob();
        var account = await Tester.Accounts.GetOwn(Tester.Session, default);
        var chatId = ChatId.Parse("testchatid1234567890");
        var key = ChatInviteSettings.GetKey(chatId);
        var kvasBackend = AppHost.Services.GetRequiredService<IServerKvasBackend>();
        await kvasBackend.ForUser(account.Id)
            .Set<ChatInviteSettings>(key, new ChatInviteSettings { InviteId = "someInviteId" });

        // act
        var userSettings = AppHost.Services.GetRequiredService<IUserSettings>();
        var value = await userSettings.Get(Tester.Session, key);
        var serverKvas = AppHost.Services.GetRequiredService<IServerKvas>();
        var data = await serverKvas.Get(Tester.Session, key);

        // assert
        value.Should().BeNull();
        data.Should().BeNull();
    }
}
