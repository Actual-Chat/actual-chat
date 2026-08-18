using ActualChat.Live;
using ActualChat.Notifications.Module;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class PttPushTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoWakeDelay = TimeSpan.FromSeconds(3);
    // Must match NotificationCollection.AppHostFixture's PttWakeTtl override.
    private static readonly TimeSpan WakeTtl = TimeSpan.FromSeconds(2);

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();
    private ApnsTestSink ApnsSink => AppHost.Services.GetRequiredService<ApnsTestSink>();
    private ILiveSessionsBackend LiveSessionsBackend => AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    private IServerKvasBackend ServerKvasBackend => AppHost.Services.GetRequiredService<IServerKvasBackend>();
    private IAuthors Authors => Tester.AppServices.GetRequiredService<IAuthors>();

    [Fact]
    public async Task ArmedByPttChatGetsWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT always-listened");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByPtt(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(() => Sink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)), WakeTimeout);
        Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task PttWithoutAnyListeningSettingsGetsWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT ptt-only");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByPtt(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(() => Sink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)), WakeTimeout);
        Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task NotArmedMemberGetsNoWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT not-armed");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task SpeakerGetsNoWake()
    {
        // arrange
        var (chatId, _, bob, bobAuthor) = await CreateChatWithAliceAndBob("WT speaker-excluded");
        var deviceId = await RegisterDevice(bob.Id, DeviceType.AndroidApp);
        await ArmByPtt(bob.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task ActiveParticipantGetsNoWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT active-participant");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByPtt(alice.Id, chatId);
        await Tester.SignIn(alice);
        var aliceAuthor = await Authors.EnsureJoined(Tester.Session, chatId, CancellationToken.None);
        await LiveSessionsBackend.SetParticipation(
            chatId, aliceAuthor.Id, ParticipationKind.AudioListen, true, CancellationToken.None);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task SecondUtteranceWithinWakeTtlIsSuppressed()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT wake-ttl");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByPtt(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);
        await WaitFor(() => Sink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)), WakeTimeout);
        Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Count(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)).Should().Be(1);
    }

    [Fact]
    public async Task SecondWakeIsSentAfterWakeTtlElapses()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT wake-ttl-expiry");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByPtt(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);
        await WaitFor(() => Sink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)), WakeTimeout);
        await Task.Delay(WakeTtl + TimeSpan.FromSeconds(1));
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(
            () => Sink.Wakes.Count(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)) >= 2,
            WakeTimeout);
        Sink.Wakes.Count(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)).Should().Be(2);
    }

    [Fact]
    public async Task MutedChatGetsNoWakeUntilUnmuted()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT muted");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByPtt(alice.Id, chatId);
        await SetNotificationMode(alice.Id, chatId, ChatNotificationMode.Muted);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));

        // act
        await SetNotificationMode(alice.Id, chatId, ChatNotificationMode.Default);
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(() => Sink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)), WakeTimeout);
        Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task FeatureFlagOffGetsNoWake()
    {
        // arrange
        await using var host = await NewAppHost("wt-flag-off", o => o with {
            ConfigureHost = (__, cfg) =>
                cfg.AddInMemory<NotificationsSettings>((x => x.EnablePttPush, "false")),
        });
        var tester = host.NewWebClientTester(Out);
        var sink = host.Services.GetRequiredService<FirebaseMessagingTestSink>();
        var liveSessionsBackend = host.Services.GetRequiredService<ILiveSessionsBackend>();
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob(tester, "WT flag-off");
        var deviceId = await RegisterDevice(host.Services.Commander(), alice.Id, DeviceType.AndroidApp);
        await ArmByPtt(host.Services, alice.Id, chatId);

        // act
        await Speak(liveSessionsBackend, chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task MemberCountOverMaxGetsNoWake()
    {
        // arrange
        await using var host = await NewAppHost("wt-member-cap", o => o with {
            ConfigureHost = (__, cfg) =>
                cfg.AddInMemory<NotificationsSettings>((x => x.PttMaxChatMembers, "1")),
        });
        var tester = host.NewWebClientTester(Out);
        var sink = host.Services.GetRequiredService<FirebaseMessagingTestSink>();
        var liveSessionsBackend = host.Services.GetRequiredService<ILiveSessionsBackend>();
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob(tester, "WT member-cap");
        var deviceId = await RegisterDevice(host.Services.Commander(), alice.Id, DeviceType.AndroidApp);
        await ArmByPtt(host.Services, alice.Id, chatId);

        // act
        await Speak(liveSessionsBackend, chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        sink.Wakes.Should().NotContain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task NonAndroidDevicesGetNoWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT web-device");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.WebBrowser);
        await ArmByPtt(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await Task.Delay(NoWakeDelay);
        Sink.Wakes.Should().NotContain(w => w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task PttDeviceIsExcludedFromMessagePushes()
    {
        // arrange
        var (chatId, alice, _, _) = await CreateChatWithAliceAndBob("WT ptt-excluded");
        var pttDeviceId = await RegisterDevice(alice.Id, DeviceType.iOSPttApp);
        var fcmDeviceId = await RegisterDevice(alice.Id, DeviceType.WebBrowser);
        Sink.Clear();

        // act: a normal message notification for alice
        await Tester.CreateTextEntry(chatId, "Hi Alice");

        // assert: the FCM push reaches the web device but never the PTT token
        await WaitFor(() => Sink.Messages.Any(m => !m.IsDismissal && m.DeviceIds.Contains(fcmDeviceId)),
            WakeTimeout);
        Sink.Messages.Should().Contain(m => !m.IsDismissal && m.DeviceIds.Contains(fcmDeviceId));
        Sink.Messages.Should().NotContain(m => m.DeviceIds.Contains(pttDeviceId));
    }

    [Fact]
    public async Task ArmedIosPttDeviceGetsApnsWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT ios-ptt");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.iOSPttApp);
        await ArmByPtt(alice.Id, chatId);
        ApnsSink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(() => ApnsSink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)),
            WakeTimeout);
        var wake = ApnsSink.Wakes.Should()
            .Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)).Subject;
        wake.ChatTitle.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DualDeviceUserGetsBothTransports()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT dual-device");
        var androidDeviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        var pttDeviceId = await RegisterDevice(alice.Id, DeviceType.iOSPttApp);
        await ArmByPtt(alice.Id, chatId);
        Sink.Clear();
        ApnsSink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(() => Sink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(androidDeviceId))
            && ApnsSink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(pttDeviceId)), WakeTimeout);
        Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(androidDeviceId));
        ApnsSink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(pttDeviceId));
    }

    // Private methods

    private static async Task WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = CpuTimestamp.Now + timeout;
        while (CpuTimestamp.Now < deadline && !condition())
            await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    private Task<(ChatId ChatId, AccountFull Alice, AccountFull Bob, Author BobAuthor)>
        CreateChatWithAliceAndBob(string title)
        => CreateChatWithAliceAndBob(Tester, title);

    private static async Task<(ChatId ChatId, AccountFull Alice, AccountFull Bob, Author BobAuthor)>
        CreateChatWithAliceAndBob(IWebClientTester tester, string title)
    {
        var alice = await tester.SignInAsAlice();
        var bob = await tester.SignInAsBob();
        var (chatId, _) = await tester.CreateChat(false, title);
        await tester.InviteToChat(chatId, alice);
        var authors = tester.AppServices.GetRequiredService<IAuthors>();
        var bobAuthor = await authors.EnsureJoined(tester.Session, chatId, CancellationToken.None);
        return (chatId, alice, bob, bobAuthor);
    }

    private Task Speak(ChatId chatId, AuthorId authorId)
        => Speak(LiveSessionsBackend, chatId, authorId);

    private static Task Speak(ILiveSessionsBackend liveSessionsBackend, ChatId chatId, AuthorId authorId)
        => liveSessionsBackend.OnStreamRegistered(chatId, authorId, null, false, true, CancellationToken.None);

    private Task ArmByPtt(UserId userId, ChatId chatId)
        => ArmByPtt(AppHost.Services, userId, chatId);

    private static async Task ArmByPtt(IServiceProvider services, UserId userId, ChatId chatId)
    {
        // Chat-level PTT is a precondition for arming; the value below is just a "turn it on"
        // sentinel - the backend stamps the real epoch, and consent must land within it.
        var chat = await services.Commander().Call(new ChatsBackend_Change(
            chatId, null, Change.Update(new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart })));
        var enabledAt = chat.PttEnabledAt!.Value;
        await services.GetRequiredService<IServerKvasBackend>()
            .ForUser(userId).UserPttSettings()
            .Update(x => x.WithPttChat(chatId, enabledAt));
    }

    private Task SetNotificationMode(UserId userId, ChatId chatId, ChatNotificationMode mode)
        => ServerKvasBackend.ForUser(userId).ChatUserSettings(chatId)
            .Update(x => x with { NotificationMode = mode });

    private Task<Symbol> RegisterDevice(UserId userId, DeviceType deviceType)
        => RegisterDevice(Commander, userId, deviceType);

    private static async Task<Symbol> RegisterDevice(ICommander commander, UserId userId, DeviceType deviceType)
    {
        var deviceId = new Symbol($"wt-device-{deviceType}-{userId.Value}");
        await commander.Call(new NotificationsBackend_RegisterDevice(userId, deviceId, deviceType, Symbol.Empty));
        return deviceId;
    }
}
