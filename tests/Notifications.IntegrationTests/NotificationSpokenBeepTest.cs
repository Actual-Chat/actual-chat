using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationSpokenBeepTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();

    [Fact]
    public async Task MonologueShouldAlertOnceAcrossSeparatePushes()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Monologue chat");
        await Tester.InviteToChat(chatId, alice);
        var deviceId = await RegisterDevice(alice.Id);
        Sink.Clear();
        await Tester.SignIn(bob);

        // act
        const int utteranceCount = 3;
        for (var i = 0; i < utteranceCount; i++) {
            var streaming = await Tester.CreateStreamingEntry(chatId, Languages.English);
            await Tester.FinalizeStreamingEntry(streaming, $"utterance {i}");
            var expectedCount = i + 1;
            await TestExt.When(() => {
                PushesTo(deviceId).Should().HaveCount(expectedCount);
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(15));
            // Past SilencePeriod, so the next utterance is a hard update with a push of its own.
            await Task.Delay(Constants.Notification.SilencePeriod + TimeSpan.FromSeconds(2));
        }

        // assert
        var pushes = PushesTo(deviceId);
        foreach (var push in pushes)
            Out.WriteLine($"push: silent={push.IsSilent}, text='{push.Notification!.Text}'");
        pushes.Should().HaveCount(utteranceCount);
        pushes[0].IsSilent.Should().BeFalse("the first utterance of a speaker run must alert");
        pushes.Skip(1).Should().OnlyContain(m => m.IsSilent,
            "later utterances of the same speaker must update the banner silently");
    }

    [Fact]
    public async Task MonologueShouldStaySilentAfterAReadOnAnotherDevice()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Read elsewhere chat");
        await Tester.InviteToChat(chatId, alice);
        var deviceId = await RegisterDevice(alice.Id);
        Sink.Clear();
        await Tester.SignIn(bob);

        var first = await Tester.CreateStreamingEntry(chatId, Languages.English);
        first = await Tester.FinalizeStreamingEntry(first, "first utterance");
        await TestExt.When(() => {
            PushesTo(deviceId).Should().ContainSingle().Which.IsSilent.Should().BeFalse();
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));

        // act: alice reads it on another device, which removes the notification...
        await Commander.Call(new ChatPositionsBackend_Set(
            alice.Id, chatId, ChatPositionKind.Read, new ChatPosition(first.ChatEntrySlim.LocalId)));
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().BeEmpty();
        }, TimeSpan.FromSeconds(10));
        await Task.Delay(Constants.Notification.SilencePeriod + TimeSpan.FromSeconds(2));

        // ...and the same speaker goes on
        var second = await Tester.CreateStreamingEntry(chatId, Languages.English);
        await Tester.FinalizeStreamingEntry(second, "second utterance");

        // assert
        await TestExt.When(() => {
            PushesTo(deviceId).Should().HaveCount(2);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
        PushesTo(deviceId)[1].IsSilent.Should().BeTrue(
            "a read on another device must not turn the next utterance of the same speaker into a first alert");
    }

    private List<FirebaseSentMessage> PushesTo(Symbol deviceId)
        => Sink.Messages.Where(m => !m.IsDismissal && m.DeviceIds.Contains(deviceId)).ToList();

    private async Task<Symbol> RegisterDevice(UserId userId)
    {
        var deviceId = new Symbol("test-device-" + userId.Value);
        await Commander.Call(new NotificationsBackend_RegisterDevice(userId, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        return deviceId;
    }
}
