using ActualChat.Notification;

namespace ActualChat.Chat.UI.Blazor.Services;

public static class NotificationsExt
{
    public static async Task<bool> IsSubscribed(
        this INotifications notifications,
        Session session,
        Symbol chatId,
        CancellationToken cancellationToken)
    {
        var status = await notifications.GetStatus(session, chatId, cancellationToken).ConfigureAwait(false);
        return status.IsSubscribed;
    }
}
