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
