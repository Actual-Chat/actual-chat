using ActualChat.Invite;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

// A null Value for a singleton settings key means the client sent a StoredSettings union tag
// this server doesn't know (an unknown tag deserializes to null, not an exception) - treating
// it as a delete silently wipes the user's settings. Parameterized "@"-prefixed keys keep
// null-as-delete (e.g. ChatInviteSettings removal).

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
        var command = new UserSettings_Set(Tester.Session, nameof(UserWalkieTalkieSettings), null);

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
        var key = ChatInviteSettings.GetKey(chatId);
        await Tester.Commander.Call(
            new UserSettings_Set(Tester.Session, key, new ChatInviteSettings { ActivationKey = "test" }));

        // act
        await Tester.Commander.Call(new UserSettings_Set(Tester.Session, key, null));

        // assert
        var settings = AppHost.Services.GetRequiredService<IUserSettings>();
        var value = await settings.Get(Tester.Session, key);
        value.Should().BeNull();
    }
}
