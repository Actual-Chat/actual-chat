using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

// The gates PttSessionCore.StartPlayback applies before a wake plays anything: the per-device
// PTT switch, the phone's own silent/vibrate/DND state, and the chat's PTT mute.

[Collection(nameof(ChatUICollection))]
public sealed class PttWakeGateTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    // Both gates short-circuit before any chat work, so an id that resolves to nothing is enough.
    private static readonly ChatId UnvisitedChatId = ChatId.Parse("testchatid1234567890");

    [Fact]
    public async Task AWakeShouldBeIgnoredWhilePttIsOffOnThisDevice()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        tester.ScopedAppServices.AppUIHub().ChatAudioUI.SetIsPttEnabledOnDevice(false);

        // act
        var reason = await StartPlayback(tester, new TestPttPlatform(), UnvisitedChatId);

        // assert
        reason.Should().Be(PttWakeIgnoreReason.DeviceDisabled);
    }

    [Fact]
    public async Task AWakeShouldBeIgnoredWhileThePhoneIsSilenced()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        tester.ScopedAppServices.AppUIHub().ChatAudioUI.SetIsPttEnabledOnDevice(true);

        // act: the device switch is on, so only the silence gate can stop this wake
        var reason = await StartPlayback(tester, new TestPttPlatform(isSilenced: true), UnvisitedChatId);

        // assert
        reason.Should().Be(PttWakeIgnoreReason.Silenced);
    }

    [Fact]
    public async Task AForegroundWakeShouldSurviveASilencedPhone()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        tester.ScopedAppServices.AppUIHub().ChatAudioUI.SetIsPttEnabledOnDevice(true);

        // act: silence governs alerts, not playback the user is already looking at
        var reason = await StartPlayback(
            tester, new TestPttPlatform(isSilenced: true), chatId, isForeground: true);

        // assert
        reason.Should().BeNull("a foreground wake is playback the user is looking at, not an alert");
    }

    [Fact]
    public async Task AWakeShouldBeIgnoredWhileTheChatIsMuted()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var (chatId, _) = await tester.CreateChat(true);
        var chat = await tester.AppServices.Commander().Call(new ChatsBackend_Change(
            chatId, null, Change.Update(new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart })));
        var now = hub.Clocks.ServerClock.Now;
        await hub.UserSettingsUI.UserPttSettings().Update(x => x
            .WithPttChat(chatId, chat.PttEnabledAt!.Value)
            .WithPttChatMuted(chatId, now, now + TimeSpan.FromHours(1)));
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(true);

        // act: even a foreground wake stays inert - muted means "don't start listening for me"
        var reason = await StartPlayback(tester, new TestPttPlatform(), chatId, isForeground: true);

        // assert
        reason.Should().Be(PttWakeIgnoreReason.Muted);
    }

    // Private methods

    private static Task<PttWakeIgnoreReason?> StartPlayback(
        BlazorTester tester,
        TestPttPlatform platform,
        ChatId chatId,
        bool isForeground = false)
        => tester.ScopedAppServices
            .GetRequiredService<PttSessionCore>()
            .StartPlayback(chatId, Moment.EpochStart, isForeground, isHeadless: false, platform);

    // Nested types

    private sealed class TestPttPlatform(bool isSilenced = false) : PttPlatform
    {
        public override bool IsSilenced { get; } = isSilenced;

        public override void OnWakeFailed(ChatId chatId)
        { }

        public override void OnHeadlessTeardown()
        { }
    }
}
