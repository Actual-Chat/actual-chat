using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public class WindowsAppIconBadge : IAppIconBadge
{
    // Toggles to "true" the first time CreateBadgeUpdaterForApplication throws in unpackaged
    // mode (AppxManifest.xml missing). Once set, we skip all subsequent attempts — every call
    // would throw another COMException, flooding the FCE log each time the unread count ticks.
    private static volatile bool _isUnsupported;

    public void SetBadgeCount(int count)
    {
        if (_isUnsupported)
            return;

        BadgeUpdater badgeUpdater;
        try {
            badgeUpdater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
        }
        catch (COMException) {
            // Unpackaged Windows app has no registered BadgeUpdater — permanently disable.
            _isUnsupported = true;
            return;
        }

        if (count <= 0)
            badgeUpdater.Clear();
        else {
            var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);

            var badgeElement = badgeXml.SelectSingleNode("/badge") as XmlElement;
            badgeElement?.SetAttribute("value", count.ToString());

            var badge = new BadgeNotification(badgeXml);
            badgeUpdater.Update(badge);
        }
    }
}
