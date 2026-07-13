using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class WalkieTalkiePushTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoWakeDelay = TimeSpan.FromSeconds(3);

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();
    private ApnsTestSink ApnsSink => AppHost.Services.GetRequiredService<ApnsTestSink>();
    private ILiveSessionsBackend LiveSessionsBackend => AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    private IServerKvasBackend ServerKvasBackend => AppHost.Services.GetRequiredService<IServerKvasBackend>();
    private IAuthors Authors => Tester.AppServices.GetRequiredService<IAuthors>();

    [Fact]
    public async Task ArmedByAlwaysListenedChatGetsWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT always-listened");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByAlwaysListened(alice.Id, chatId);
        Sink.Clear();

        // act
        await Speak(chatId, bobAuthor.Id);

        // assert
        await WaitFor(() => Sink.Wakes.Any(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId)), WakeTimeout);
        Sink.Wakes.Should().Contain(w => w.ChatId == chatId && w.DeviceIds.Contains(deviceId));
    }

    [Fact]
    public async Task ArmedByForeverListeningModeGetsWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT forever-mode");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.AndroidApp);
        await ArmByForeverListeningMode(alice.Id, chatId);
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
        await ArmByAlwaysListened(bob.Id, chatId);
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
        await ArmByAlwaysListened(alice.Id, chatId);
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
        await ArmByAlwaysListened(alice.Id, chatId);
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
    public async Task NonAndroidDevicesGetNoWake()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("WT web-device");
        var deviceId = await RegisterDevice(alice.Id, DeviceType.WebBrowser);
        await ArmByAlwaysListened(alice.Id, chatId);
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
        await ArmByAlwaysListened(alice.Id, chatId);
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
        await ArmByAlwaysListened(alice.Id, chatId);
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

    private async Task<(ChatId ChatId, AccountFull Alice, AccountFull Bob, Author BobAuthor)>
        CreateChatWithAliceAndBob(string title)
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, title);
        await Tester.InviteToChat(chatId, alice);
        var bobAuthor = await Authors.EnsureJoined(Tester.Session, chatId, CancellationToken.None);
        return (chatId, alice, bob, bobAuthor);
    }

    private Task Speak(ChatId chatId, AuthorId authorId)
        => LiveSessionsBackend.OnStreamRegistered(chatId, authorId, null, false, true, CancellationToken.None);

    private Task ArmByAlwaysListened(UserId userId, ChatId chatId)
        => ServerKvasBackend.ForUser(userId).UserListeningSettings()
            .Update(x => x.WithAlwaysListeningChat(chatId));

    private Task ArmByForeverListeningMode(UserId userId, ChatId chatId)
        => ServerKvasBackend.ForUser(userId).ChatUserSettings(chatId)
            .Update(x => x with { ListeningMode = ListeningMode.Forever });

    private async Task<Symbol> RegisterDevice(UserId userId, DeviceType deviceType)
    {
        var deviceId = new Symbol($"wt-device-{deviceType}-{userId.Value}");
        await Commander.Call(new NotificationsBackend_RegisterDevice(userId, deviceId, deviceType, Symbol.Empty));
        return deviceId;
    }
}
