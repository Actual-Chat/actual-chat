using ActualChat.UI;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="SystemSettingsUI"/> that opens platform system settings.
/// </summary>
public class MauiSystemSettingsUI : SystemSettingsUI
{
    [method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiSystemSettingsUI))]
    public MauiSystemSettingsUI() { }

    public override Task Open(SystemSettingsSection section = SystemSettingsSection.App)
    {
        // AppInfo.ShowSettingsUI() deep-links to the iOS per-app Settings page (app-settings:),
        // which has no handler on macOS — a silent no-op on Mac Catalyst. Open the matching
        // System Settings privacy pane directly instead so users can unblock access.
        if (OperatingSystem.IsMacCatalyst())
            return MauiBrowser.Open(MacPrivacyPaneUrl(section));

        AppInfo.Current.ShowSettingsUI();
        return Task.CompletedTask;
    }

    private static string MacPrivacyPaneUrl(SystemSettingsSection section)
    {
        const string prefix = "x-apple.systempreferences:com.apple.preference.security?";
        return section switch {
            SystemSettingsSection.Camera => $"{prefix}Privacy_Camera",
            SystemSettingsSection.Microphone => $"{prefix}Privacy_Microphone",
            _ => $"{prefix}Privacy",
        };
    }
}
