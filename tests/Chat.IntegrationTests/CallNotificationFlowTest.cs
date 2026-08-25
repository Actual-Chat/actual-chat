using ActualChat.Notifications;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class CallNotificationFlowTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();

    [Fact]
    public async Task CancelledRingIsRemovedFromActiveNotifications()
    {
        // arrange — Bob (caller) and Alice (callee) share a private group chat
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        var aliceUser = await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var notifications = alice.AppServices.GetRequiredService<INotifications>();
        var deviceId = new Symbol("test-device-" + aliceUser.Id.Value);
        await Commander.Call(
            new NotificationsBackend_RegisterDevice(aliceUser.Id, deviceId, DeviceType.WebBrowser, Symbol.Empty));

        // act — Bob rings Alice
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // assert — the ring lands in Alice's active set (StartCall → NotifyCall → Notify → Items)
        CallNotification ring = null!;
        await ComputedTest.When(async ct => {
            var active = await notifications.ListActive(alice.Session, ct);
            ring = active.OfType<CallNotification>().Should().ContainSingle(n => n.ChatId == chatId).Subject;
        }, TimeSpan.FromSeconds(15));

        // act — Bob cancels before Alice answers
        Sink.Clear();
        await backend.CancelCall(chatId, bobAuthor.Id, default);

        // assert — a cancelled ring should leave the active set, not just get an on-device dismissal
        await ComputedTest.When(async ct => {
            var active = await notifications.ListActive(alice.Session, ct);
            active.OfType<CallNotification>().Should().BeEmpty();
        }, TimeSpan.FromSeconds(15));

        // assert — handling it must still push the dismissal that closes the banner on the device,
        // which is what makes NotificationsBackend_Dismiss a valid replacement for a raw PushDismissal
        await TestExt.When(() => {
            Sink.Messages
                .Where(m => m.IsDismissal && m.DeviceIds.Contains(deviceId))
                .Should().Contain(m => m.DismissedIds.Contains(ring.Id));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(15));
    }
}
