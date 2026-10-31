using ActualChat.UI.Blazor.App.Services;
using UserNotifications;

namespace ActualChat.App.Maui;

public class AppIconBadge : IAppIconBadge
{
    public void SetBadgeCount(int count)
        => UNUserNotificationCenter.Current.SetBadgeCount(count, null);
}
