using ActualChat.Hosting;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class PttForegroundNavigationTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ForegroundingDuringPttPlaybackShouldOpenThatChatAndHideTheList()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        hub.BrowserInfo.OnScreenSizeChanged(nameof(ScreenSize.Small), false, 800);
        tester.RenderModalHost(hub);
        hub.BrowserInfo.OnIsVisibleChanged(false);
        var chatId = await ArmChat(tester, hub);
        var player = await StartListening(hub, chatId);
        ((MutableState<bool>)player.Playback.IsPlaying).Set(true);
        var backgroundState = hub.Services.GetRequiredService<BackgroundStateTracker>();
        await TestWait.When(async ct => (await backgroundState.IsBackground.Use(ct)).Should().BeTrue());

        // act
        hub.BrowserInfo.OnIsVisibleChanged(true);

        // assert
        await TestWait.When(async ct => (await hub.PanelsUI.Left.IsVisible.Use(ct)).Should().BeFalse());
        var url = await hub.AutoNavigationUI.GetAutoNavigationUrl();
        url.Should().Be(Links.Chat(chatId));
    }

    [Fact]
    public async Task RecordingShouldTakePriorityOverOtherPttChats()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var listeningChatId = await ArmChat(tester, hub);
        var recordingChatId = await ArmChat(tester, hub);
        await hub.ChatAudioUI.SetListeningState(listeningChatId, true);
        await hub.ChatAudioUI.SetRecordingChatId(recordingChatId, isPtt: true);

        // act
        var target = await hub.ChatAudioUI.GetForegroundChatId(CancellationToken.None);

        // assert
        target.Should().Be(recordingChatId);
    }

    [Fact]
    public async Task ArmedButIdleChatsShouldNotCauseNavigation()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var chatId = await ArmChat(tester, hub);
        await hub.ChatAudioUI.SetListeningState(chatId, true);

        // act
        var target = await hub.ChatAudioUI.GetForegroundChatId(CancellationToken.None);

        // assert
        target.Should().BeNull();
    }

    [Fact]
    public async Task PttArrivingWhileAlreadyForegroundShouldNotChangeTheScreen()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        hub.BrowserInfo.OnScreenSizeChanged(nameof(ScreenSize.Small), false, 800);
        tester.RenderModalHost(hub);
        hub.BrowserInfo.OnIsVisibleChanged(true);
        var chatId = await ArmChat(tester, hub);
        var player = await StartListening(hub, chatId);

        // act
        ((MutableState<bool>)player.Playback.IsPlaying).Set(true);

        // assert
        (await hub.ChatAudioUI.GetForegroundChatId(CancellationToken.None)).Should().Be(chatId);
        var url = await hub.AutoNavigationUI.GetAutoNavigationUrl();
        url.Should().NotBe(Links.Chat(chatId));
        hub.PanelsUI.Left.IsVisible.Value.Should().BeTrue();
    }

    [Fact]
    public async Task HeadlessRecordingNavigationShouldBeQueuedForInitialNavigation()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        hub.BrowserInfo.OnScreenSizeChanged(nameof(ScreenSize.Small), false, 800);
        tester.RenderModalHost(hub);
        var chatId = await ArmChat(tester, hub);

        // act
        await hub.ChatAudioUI.NavigateToForegroundChat(chatId);

        // assert
        var url = await hub.AutoNavigationUI.GetAutoNavigationUrl();
        url.Should().Be(Links.Chat(chatId));
        hub.PanelsUI.Left.IsVisible.Value.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MutedOrPausedPttShouldNotBecomeTheForegroundTarget(bool isMuted, bool isPaused)
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var chatId = await ArmChat(tester, hub);
        var player = await StartListening(hub, chatId);
        ((MutableState<bool>)player.Playback.IsPlaying).Set(true);
        if (isMuted) {
            var now = hub.Clocks.ServerClock.Now;
            await hub.UserSettingsUI.UserPttSettings()
                .Update(x => x.WithPttChatMuted(chatId, now, now + TimeSpan.FromHours(1)));
        }
        if (isPaused) {
            var pauseCmd = new ChatsBackend_Change(chatId, null, Change.Update(new ChatDiff { IsPttPaused = true }));
            await tester.AppServices.Commander().Call(pauseCmd);
        }
        await TestWait.When(async ct => (await hub.ChatAudioUI.GetPttChatIds(ct)).Should().BeEmpty());

        // act
        var target = await hub.ChatAudioUI.GetForegroundChatId(CancellationToken.None);

        // assert
        target.Should().BeNull();
    }

    [Fact]
    public async Task MostRecentAudiblePttChatShouldBeSelected()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var firstId = await ArmChat(tester, hub);
        var secondId = await ArmChat(tester, hub);
        var firstPlayer = await StartListening(hub, firstId);
        var secondPlayer = await StartListening(hub, secondId);
        ((MutableState<bool>)firstPlayer.Playback.IsPlaying).Set(true);
        ((MutableState<bool>)secondPlayer.Playback.IsPlaying).Set(true);
        var now = hub.Clocks.ServerClock.Now;
        hub.VoiceActivityUI.NoteIncomingVoice(firstId, now - TimeSpan.FromSeconds(1));
        hub.VoiceActivityUI.NoteIncomingVoice(secondId, now);

        // act
        var target = await hub.ChatAudioUI.GetForegroundChatId(CancellationToken.None);

        // assert
        target.Should().Be(secondId);
    }

    // Private methods

    private static async Task<ChatId> ArmChat(BlazorTester tester, AppUIHub hub)
    {
        var (chatId, _) = await tester.CreateChat(true);
        var enableCmd = new ChatsBackend_Change(
            chatId, null, Change.Update(new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart }));
        var chat = await tester.AppServices.Commander().Call(enableCmd);
        await hub.ChatAudioUI.ConsentPtt(chatId, chat.PttEnabledAt!.Value, CancellationToken.None);
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(true);
        return chatId;
    }

    private static async Task<ChatListeningPlayer> StartListening(AppUIHub hub, ChatId chatId)
    {
        hub.ChatAudioUI.Enable();
        await hub.ChatAudioUI.SetListeningState(chatId, true);
        return await TestWait.When(async ct => {
            var player = await hub.ChatAudioUI.GetListeningPlayer(chatId, ct);
            player.Should().NotBeNull();
            return player!;
        });
    }
}
