using ActualChat.Live;
using ActualChat.Notifications;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class CallNotificationFlowTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task CancelledRingIsRemovedFromActiveNotifications()
    {
        // arrange — Bob (caller) and Alice (callee) share a private group chat
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = bob.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var notifications = alice.AppServices.GetRequiredService<INotifications>();

        // act — Bob rings Alice
        await backend.StartCall(
            chatId, bobAuthor!.Id, new[] { aliceAuthor!.Id }.ToApiArray(), false, default);

        // assert — the ring lands in Alice's active set (StartCall → NotifyCall → Notify → Displayed)
        await ComputedTest.When(async ct => {
            var active = await notifications.ListActive(alice.Session, ct);
            active.OfType<CallNotification>().Should().ContainSingle(n => n.ChatId == chatId);
        }, TimeSpan.FromSeconds(15));

        // act — Bob cancels before Alice answers
        await backend.CancelCall(chatId, bobAuthor.Id, default);

        // assert — a cancelled ring should leave the active set, not just get an on-device dismissal
        await ComputedTest.When(async ct => {
            var active = await notifications.ListActive(alice.Session, ct);
            active.OfType<CallNotification>().Should().BeEmpty();
        }, TimeSpan.FromSeconds(15));
    }
}
