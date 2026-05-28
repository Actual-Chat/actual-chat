using ActualChat.UI.Blazor.App.Services;
using UserNotifications;

namespace ActualChat.App.Maui;

public class IosAppIconBadge : IAppIconBadge
{
    public void SetUnreadChatCount(int count)
        => UNUserNotificationCenter.Current.SetBadgeCount(count, null);
}
