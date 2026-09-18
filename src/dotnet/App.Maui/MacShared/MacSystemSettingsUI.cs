using ActualChat.UI;

namespace ActualChat.App.Maui;

/// <summary>
/// Opens the requested Privacy &amp; Security pane: AppInfo.ShowSettingsUI only activates
/// System Settings on the Mac, which then shows its default pane - Touch ID &amp; Password on most Macs.
/// </summary>
public sealed class MacSystemSettingsUI : SystemSettingsUI
{
    public override Task Open(SystemSettingsPane pane)
        => Launcher.Default.OpenAsync(GetPaneUrl(pane));

    // Private methods

    private static string GetPaneUrl(SystemSettingsPane pane)
        => "x-apple.systempreferences:" + pane switch {
            SystemSettingsPane.Microphone => "com.apple.preference.security?Privacy_Microphone",
            SystemSettingsPane.Camera => "com.apple.preference.security?Privacy_Camera",
            SystemSettingsPane.Contacts => "com.apple.preference.security?Privacy_Contacts",
            SystemSettingsPane.Location => "com.apple.preference.security?Privacy_LocationServices",
            SystemSettingsPane.Notifications => "com.apple.Notifications-Settings.extension",
            SystemSettingsPane.Photos => "com.apple.preference.security?Privacy_Photos",
            _ => "com.apple.preference.security",
        };
}
