using ActualChat.Queues;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationActiveReaderTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();

    // A present reader who is caught up to the head is held for the grace window, and if they read
    // the message within it, it never alerts on any device (no SendMessage at all).
    [Fact]
    public async Task CaughtUpMessageIsSwallowedWhenReadWithinGrace()
    {
        var (alice, chatId) = await SetUpAliceInChat("Active reader — swallow");
        await Tester.ForcePresent(alice);
        // A registered device makes the "no push" assertion meaningful — a real send target exists.
        await RegisterDevice(alice.Id);

        const long entryLid = 1000;
        await SetReadPosition(alice.Id, chatId, entryLid - 1);
        Sink.Clear();

        var notification = NewMessageNotification(alice.Id, chatId, entryLid, "Live message");
        await Enqueue(notification);

        // Read it within the grace window — the deferred re-check must then drop it silently.
        await SetReadPosition(alice.Id, chatId, entryLid);

        await Task.Delay(Constants.Notification.ActiveReaderGrace + TimeSpan.FromSeconds(5));

        var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
        info.Displayed.Should().BeEmpty();
        Sink.Messages.Should().NotContain(m => m.Notification != null && m.Notification.Id == notification.Id,
            "a message read during the grace window must never be pushed to any device");
    }

    // The same held message is not lost: if the reader does not read it, it alerts after the grace
    // window instead of immediately.
    [Fact]
    public async Task CaughtUpMessageAlertsAfterGraceWhenUnread()
    {
        var (alice, chatId) = await SetUpAliceInChat("Active reader — no loss");
        await Tester.ForcePresent(alice);
        var deviceId = await RegisterDevice(alice.Id);

        const long entryLid = 1000;
        await SetReadPosition(alice.Id, chatId, entryLid - 1);
        Sink.Clear();

        var notification = NewMessageNotification(alice.Id, chatId, entryLid, "Live message");
        await Enqueue(notification);

        // Held, not alerted: nothing is displayed while the grace window runs.
        await Task.Delay(TimeSpan.FromSeconds(3));
        var early = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
        early.Displayed.Should().BeEmpty("the message must be deferred, not pushed immediately");

        // After the grace window the unread message surfaces and a real push goes out.
        await TestExt.When(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Displayed.Should().ContainSingle().Which.Id.Should().Be(notification.Id);
            Sink.Messages.Should().Contain(m =>
                !m.IsDismissal && !m.IsSilent && m.Notification != null
                && m.Notification.Id == notification.Id && m.DeviceIds.Contains(deviceId));
        }, Constants.Notification.ActiveReaderGrace + TimeSpan.FromSeconds(10));
    }

    // bob owns the chat (so he is the message author) and alice is an invited, deliverable member.
    private async Task<(AccountFull Alice, ChatId ChatId)> SetUpAliceInChat(string title)
    {
        var alice = await Tester.SignInAsAlice();
        await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, title);
        await Tester.InviteToChat(chatId, alice);
        return (alice, chatId);
    }

    // Submitted through the queue (as production does) so the deferred OnProcess runs in the queue
    // consumer's context — a direct Commander.Call would leak its disposed scope into the timer.
    private Task Enqueue(Notification notification)
        => AppHost.Services.Queues().Enqueue(new NotificationsBackend_Notify(notification));

    private static MessageNotification NewMessageNotification(UserId userId, ChatId chatId, long entryLid, string text)
    {
        var authorId = AuthorId.New(chatId, 1);
        return MessageNotification.New(userId, chatId, entryLid, authorId) with {
            Title = $"Bob @ {chatId.Value}",
            Text = text,
            StartEntryLid = entryLid,
            UnreadCount = 1,
            AuthorIds = new[] { authorId }.ToApiArray(),
            LeadText = text,
            LeadCount = 1,
            SentAt = Moment.Now,
        };
    }

    private Task SetReadPosition(UserId userId, ChatId chatId, long entryLid)
        => Commander.Call(new ChatPositionsBackend_Set(
            userId, chatId, ChatPositionKind.Read, new ChatPosition(entryLid)));

    private async Task<Symbol> RegisterDevice(UserId userId)
    {
        var deviceId = new Symbol("test-device-" + userId.Value);
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            userId, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        return deviceId;
    }
}
