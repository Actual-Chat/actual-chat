using ActualChat.Notifications;

namespace ActualChat.Testing.Host;

public static class NotificationOperations
{
    public static async Task<ChatEntryRelatedNotification> WaitForChatEntryNotification(
        this IWebClientTester tester,
        UserId userId,
        ChatEntryId entryId,
        TimeSpan? timeout = null)
    {
        ChatEntryRelatedNotification notification = null!;
        await TestExt.When(async () => {
            var info = await tester.NotificationsBackend.GetUserNotificationInfo(userId, CancellationToken.None);
            var notifications = info.Items
                .OfType<ChatEntryRelatedNotification>()
                .Where(x => x.EntryId == entryId)
                .ToList();
            notifications.Should().HaveCount(1);
            notification = notifications[0];
        }, timeout ?? TimeSpan.FromSeconds(30));
        return notification;
    }
}
