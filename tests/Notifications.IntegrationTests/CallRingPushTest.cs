using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class CallRingPushTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NoPushDelay = TimeSpan.FromSeconds(3);

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();
    private ApnsTestSink ApnsSink => AppHost.Services.GetRequiredService<ApnsTestSink>();
    private IAuthors Authors => Tester.AppServices.GetRequiredService<IAuthors>();

    [Fact]
    public async Task VoipDeviceRingsAndItsOwnFcmBannerIsSuppressed()
    {
        // arrange
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("Call ring - voip");
        var sessionHash = new Symbol("call-session-voip");
        var fcmDeviceId = await RegisterDevice(alice.Id, DeviceType.iOSApp, sessionHash);
        var voipDeviceId = await RegisterDevice(alice.Id, DeviceType.iOSVoipApp, sessionHash);
        await Tester.SignIn(alice);
        var aliceAuthor = await Authors.EnsureJoined(Tester.Session, chatId, CancellationToken.None);
        Sink.Clear();
        ApnsSink.Clear();

        // act
        await Commander.Call(new NotificationsBackend_NotifyCall(
            ConversationId.New(chatId, 1), bobAuthor.Id, [aliceAuthor.Id], false));

        // assert
        await WaitFor(() => ApnsSink.CallRings.Any(r => r.DeviceIds.Contains(voipDeviceId)), RingTimeout);
        var ring = ApnsSink.CallRings.Should()
            .ContainSingle(r => r.DeviceIds.Contains(voipDeviceId)).Subject;
        ring.Caller.Should().Be(bobAuthor.Id);
        ring.CallerName.Should().Be($"{bobAuthor.Avatar.Name} @ Call ring - voip");
        ring.ConversationId.ChatId.Should().Be(chatId);
        ring.HasVideo.Should().BeFalse();

        await Task.Delay(NoPushDelay);
        Sink.Messages.Should().NotContain(m => m.DeviceIds.Contains(fcmDeviceId));
    }

    [Fact]
    public async Task BannerStillGoesOutWhenApnsIsNotConfigured()
    {
        // arrange: an unconfigured APNs client must not silence the phone it can't ring.
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("Call ring - no apns");
        var sessionHash = new Symbol("call-session-no-apns");
        var fcmDeviceId = await RegisterDevice(alice.Id, DeviceType.iOSApp, sessionHash);
        await RegisterDevice(alice.Id, DeviceType.iOSVoipApp, sessionHash);
        await Tester.SignIn(alice);
        var aliceAuthor = await Authors.EnsureJoined(Tester.Session, chatId, CancellationToken.None);
        Sink.Clear();
        ApnsSink.Clear();
        ApnsSink.IsConfigured = false;
        try {
            // act
            await Commander.Call(new NotificationsBackend_NotifyCall(
                ConversationId.New(chatId, 1), bobAuthor.Id, [aliceAuthor.Id], false));

            // assert
            await WaitFor(
                () => Sink.Messages.Any(m => !m.IsDismissal && m.DeviceIds.Contains(fcmDeviceId)), RingTimeout);
            Sink.Messages.Should().Contain(m => !m.IsDismissal && m.DeviceIds.Contains(fcmDeviceId));
        }
        finally {
            ApnsSink.IsConfigured = true;
        }
    }

    [Fact]
    public async Task BannerStillGoesOutWhenTheRingFails()
    {
        // arrange: a ring that never left the server must not cost the phone its banner too.
        var (chatId, alice, _, bobAuthor) = await CreateChatWithAliceAndBob("Call ring - failed ring");
        var sessionHash = new Symbol("call-session-ring-failure");
        var fcmDeviceId = await RegisterDevice(alice.Id, DeviceType.iOSApp, sessionHash);
        await RegisterDevice(alice.Id, DeviceType.iOSVoipApp, sessionHash);
        await Tester.SignIn(alice);
        var aliceAuthor = await Authors.EnsureJoined(Tester.Session, chatId, CancellationToken.None);
        Sink.Clear();
        ApnsSink.Clear();
        ApnsSink.MustFailCallRings = true;
        try {
            // act
            await Commander.Call(new NotificationsBackend_NotifyCall(
                ConversationId.New(chatId, 1), bobAuthor.Id, [aliceAuthor.Id], false));

            // assert
            await WaitFor(
                () => Sink.Messages.Any(m => !m.IsDismissal && m.DeviceIds.Contains(fcmDeviceId)), RingTimeout);
            Sink.Messages.Should().Contain(m => !m.IsDismissal && m.DeviceIds.Contains(fcmDeviceId));
        }
        finally {
            ApnsSink.MustFailCallRings = false;
        }
    }

    [Fact]
    public async Task PeerCallRingsWithTheCallerName()
    {
        // arrange: a peer chat has no title of its own, so a ring headlined by the chat had no name.
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var chatId = PeerChatId.New(alice.Id, bob.Id);
        // The peer chat exists on the backend only once something has been posted to it.
        await Tester.CreateTextEntry(chatId, "Hello peer!");
        var bobAuthor = await Authors.EnsureJoined(Tester.Session, chatId, CancellationToken.None);
        var voipDeviceId = await RegisterDevice(alice.Id, DeviceType.iOSVoipApp, "call-session-peer");
        await Tester.SignIn(alice);
        var aliceAuthor = await Authors.EnsureJoined(Tester.Session, chatId, CancellationToken.None);
        ApnsSink.Clear();

        // act
        await Commander.Call(new NotificationsBackend_NotifyCall(
            ConversationId.New(chatId, 1), bobAuthor.Id, [aliceAuthor.Id], false));

        // assert
        await WaitFor(() => ApnsSink.CallRings.Any(r => r.DeviceIds.Contains(voipDeviceId)), RingTimeout);
        var ring = ApnsSink.CallRings.Should()
            .ContainSingle(r => r.DeviceIds.Contains(voipDeviceId)).Subject;
        ring.CallerName.Should().Be(bobAuthor.Avatar.Name);
    }

    [Fact]
    public async Task ReRegisteringADeviceRefreshesItsSessionHash()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var deviceId = new Symbol($"call-device-rehash-{alice.Id.Value}");
        var backend = AppHost.Services.GetRequiredService<INotificationsBackend>();

        // act
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            alice.Id, deviceId, DeviceType.iOSVoipApp, "session-1"));
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            alice.Id, deviceId, DeviceType.iOSVoipApp, "session-2"));

        // assert
        var sessionHash = await WaitForSessionHash(backend, alice.Id, deviceId, "session-2");
        sessionHash.Should().Be("session-2", "a re-signed-in device must carry its new session hash");
    }

    [Fact]
    public async Task ReRegisteringAVoipTokenUnderAnotherAccountShouldRebindIt()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var deviceId = new Symbol($"call-device-rebind-{alice.Id.Value}");
        var backend = AppHost.Services.GetRequiredService<INotificationsBackend>();
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            bob.Id, deviceId, DeviceType.iOSApp, "session-bob"));

        // act
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            alice.Id, deviceId, DeviceType.iOSVoipApp, "session-alice"));

        // assert
        var device = await WaitForDevice(backend, alice.Id, deviceId);
        device.Should().NotBeNull("a PushKit token belongs to the installation, so it follows the account signed in on it");
        device!.DeviceType.Should().Be(DeviceType.iOSVoipApp, "a row registered before the VoIP type existed must take it");
        device.SessionHash.Value.Should().Be("session-alice");
        var bobDevices = await backend.ListDevices(bob.Id, CancellationToken.None);
        bobDevices.Should().NotContain(d => d.DeviceId == deviceId, "the previous owner must not get rings on it");
    }

    [Fact]
    public async Task ReRegisteringAnFcmTokenUnderAnotherAccountShouldKeepTheOwner()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var deviceId = new Symbol($"call-device-keep-owner-{alice.Id.Value}");
        var backend = AppHost.Services.GetRequiredService<INotificationsBackend>();
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            bob.Id, deviceId, DeviceType.iOSApp, "session-bob"));
        (await WaitForDevice(backend, bob.Id, deviceId)).Should().NotBeNull();

        // act
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            alice.Id, deviceId, DeviceType.iOSApp, "session-alice"));

        // assert
        await Task.Delay(NoPushDelay);
        var bobDevices = await backend.ListDevices(bob.Id, CancellationToken.None);
        bobDevices.Should().Contain(d => d.DeviceId == deviceId, "an FCM token owned by another account is not re-bound");
        var aliceDevices = await backend.ListDevices(alice.Id, CancellationToken.None);
        aliceDevices.Should().NotContain(d => d.DeviceId == deviceId);
    }

    // Private methods

    private static async Task<Device?> WaitForDevice(INotificationsBackend backend, UserId userId, Symbol deviceId)
    {
        var deadline = CpuTimestamp.Now + RingTimeout;
        while (CpuTimestamp.Now < deadline) {
            var devices = await backend.ListDevices(userId, CancellationToken.None);
            if (devices.FirstOrDefault(d => d.DeviceId == deviceId) is { } device)
                return device;

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return null;
    }

    private static async Task<string> WaitForSessionHash(
        INotificationsBackend backend, UserId userId, Symbol deviceId, string expected)
    {
        var deadline = CpuTimestamp.Now + RingTimeout;
        var sessionHash = "";
        while (CpuTimestamp.Now < deadline) {
            var devices = await backend.ListDevices(userId, CancellationToken.None);
            sessionHash = devices.FirstOrDefault(d => d.DeviceId == deviceId)?.SessionHash.Value ?? "";
            if (sessionHash == expected)
                break;

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return sessionHash;
    }

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

    private Task<Symbol> RegisterDevice(UserId userId, DeviceType deviceType, Symbol sessionHash)
        => RegisterDevice(Commander, userId, deviceType, sessionHash);

    private static async Task<Symbol> RegisterDevice(
        ICommander commander, UserId userId, DeviceType deviceType, Symbol sessionHash)
    {
        var deviceId = new Symbol($"call-device-{deviceType}-{userId.Value}");
        await commander.Call(new NotificationsBackend_RegisterDevice(userId, deviceId, deviceType, sessionHash));
        return deviceId;
    }
}
