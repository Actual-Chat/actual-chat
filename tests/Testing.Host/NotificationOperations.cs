using ActualChat.Notifications;

namespace ActualChat.Testing.Host;

public static class NotificationOperations
{
    public static async Task<ChatNotificationItem> WaitForChatEntryNotification(
        this IWebClientTester tester,
        UserId userId,
        ChatEntryId entryId,
        TimeSpan? timeout = null)
    {
        var clocks = tester.AppServices.Clocks();
        ChatNotificationItem notification = null!;
        await TestExt.When(async () => {
            var ids = await tester.NotificationsBackend.ListRecentNotificationIds(
                userId, clocks.SystemClock.Now - TimeSpan.FromMinutes(1), CancellationToken.None);
            ids.Should().NotBeEmpty();
            var retrieved = await ids
                .Select(x => tester.NotificationsBackend.Get(x, CancellationToken.None))
                .Collect();
            var notifications = retrieved
                .SkipNullItems()
                .OfType<ChatNotificationItem>()
                .Where(x => x.EntryId == entryId)
                .ToList();
            notifications.Should().HaveCount(1);
            notification = notifications[0];
        }, timeout ?? TimeSpan.FromSeconds(30));
        return notification;
    }
}
