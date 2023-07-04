using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public class WindowsAppIconBadge : IAppIconBadge
{
    public void SetUnreadChatCount(int count)
    {
        var badgeUpdater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
        if (count <= 0)
            badgeUpdater.Clear();
        else {
            var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);

            var badgeElement = badgeXml.SelectSingleNode("/badge") as XmlElement;
            badgeElement?.SetAttribute("value", count.ToString(CultureInfo.InvariantCulture));

            var badge = new BadgeNotification(badgeXml);
            badgeUpdater.Update(badge);
        }
    }
}
