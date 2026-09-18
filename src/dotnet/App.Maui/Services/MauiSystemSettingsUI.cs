using ActualChat.UI;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="SystemSettingsUI"/> that opens platform system settings.
/// </summary>
public class MauiSystemSettingsUI : SystemSettingsUI
{
    [method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiSystemSettingsUI))]
    public MauiSystemSettingsUI() { }

    public override Task Open(SystemSettingsPane pane)
    {
#if MACOS || MACCATALYST
        // AppInfo.ShowSettingsUI just activates System Settings on the Mac, which then shows
        // whatever pane it opens with - Touch ID & Password on most Macs
        return Launcher.Default.OpenAsync(GetMacPaneUrl(pane));
#else
        AppInfo.Current.ShowSettingsUI();
        return Task.CompletedTask;
#endif
    }

#if MACOS || MACCATALYST
    // Private methods

    private static string GetMacPaneUrl(SystemSettingsPane pane)
        => "x-apple.systempreferences:" + pane switch {
            SystemSettingsPane.Microphone => "com.apple.preference.security?Privacy_Microphone",
            SystemSettingsPane.Camera => "com.apple.preference.security?Privacy_Camera",
            SystemSettingsPane.Contacts => "com.apple.preference.security?Privacy_Contacts",
            SystemSettingsPane.Location => "com.apple.preference.security?Privacy_LocationServices",
            SystemSettingsPane.Notifications => "com.apple.Notifications-Settings.extension",
            SystemSettingsPane.Photos => "com.apple.preference.security?Privacy_Photos",
            _ => "com.apple.preference.security",
        };
#endif
}
