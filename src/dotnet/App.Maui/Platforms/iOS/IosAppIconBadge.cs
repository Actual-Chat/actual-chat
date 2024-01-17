using ActualChat.UI.Blazor.App.Services;
using UIKit;

namespace ActualChat.App.Maui;

public class IosAppIconBadge : IAppIconBadge
{
    public void SetUnreadChatCount(int count)
        => UIApplication.SharedApplication.ApplicationIconBadgeNumber = count;
}
