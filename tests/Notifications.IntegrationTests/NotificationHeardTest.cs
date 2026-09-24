using ActualChat.Queues;
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationHeardTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private FirebaseMessagingTestSink Sink => AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();

    [Fact]
    public async Task HeardMessageShouldNotAlert()
    {
        // arrange
        // A PTT utterance played hands-free acks itself via the Heard position, so the
        // "message completed" notification that follows it must never reach a device.
        var (alice, chatId) = await SetUpAliceInChat("Heard — no alert");
        var deviceId = await RegisterDevice(alice.Id);
        const long entryLid = 1000;
        await SetHeardPosition(alice.Id, chatId, entryLid);
        Sink.Clear();

        // act
        var notification = NewMessageNotification(alice.Id, chatId, entryLid, "Already heard");
        await Enqueue(notification);
        await AppHost.Services.Queues().WhenProcessing();

        // assert
        var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
        info.Items.Should().BeEmpty("an utterance the recipient already heard is not unread");
        Sink.Messages.Should().NotContain(
            m => !m.IsDismissal && m.Notification != null && m.Notification.Id == notification.Id
                && m.DeviceIds.Contains(deviceId),
            "a heard utterance must not raise a message notification");
    }

    [Fact]
    public async Task HeardAdvanceShouldHideActiveNotification()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, "Heard — hide active");
        await Tester.InviteToChat(chatId, alice);
        await Tester.SignIn(bob);
        var entry = await Tester.CreateTextEntry(chatId, "Hello Alice!");
        await TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().ContainSingle();
        }, TimeSpan.FromSeconds(10));

        // act
        await SetHeardPosition(alice.Id, chatId, entry.LocalId);

        // assert
        await TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().BeEmpty("the heard entry must be hidden by the population re-check");
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task HeardBeforeTheEntryShouldStillAlert()
    {
        // arrange
        // The watermark is forward-only and per entry: hearing up to the previous entry says
        // nothing about this one, so it must alert as usual.
        var (alice, chatId) = await SetUpAliceInChat("Heard — earlier entry");
        var deviceId = await RegisterDevice(alice.Id);
        const long entryLid = 1000;
        await SetHeardPosition(alice.Id, chatId, entryLid - 1);
        Sink.Clear();

        // act
        var notification = NewMessageNotification(alice.Id, chatId, entryLid, "Not heard yet");
        await Enqueue(notification);

        // assert
        await TestWait.WhenPolled(async () => {
            var info = await Tester.NotificationsBackend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().ContainSingle().Which.Id.Should().Be(notification.Id);
            Sink.Messages.Should().Contain(m =>
                !m.IsDismissal && !m.IsSilent && m.Notification != null
                && m.Notification.Id == notification.Id && m.DeviceIds.Contains(deviceId));
        }, TimeSpan.FromSeconds(10));
    }

    private async Task<(AccountFull Alice, ChatId ChatId)> SetUpAliceInChat(string title)
    {
        // bob owns the chat (so he is the message author) and alice is an invited, deliverable member.
        var alice = await Tester.SignInAsAlice();
        await Tester.SignInAsBob();
        var (chatId, _) = await Tester.CreateChat(false, title);
        await Tester.InviteToChat(chatId, alice);
        return (alice, chatId);
    }

    private Task Enqueue(Notification notification)
        // Submitted through the queue (as production does) so the deferred OnProcess runs in the queue
        // consumer's context — a direct Commander.Call would leak its disposed scope into the timer.
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

    private Task SetHeardPosition(UserId userId, ChatId chatId, long entryLid)
        => Commander.Call(new ChatPositionsBackend_Set(
            userId, chatId, ChatPositionKind.Heard, new ChatPosition(entryLid)));

    private async Task<Symbol> RegisterDevice(UserId userId)
    {
        var deviceId = new Symbol("test-device-" + userId.Value);
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            userId, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        return deviceId;
    }
}
