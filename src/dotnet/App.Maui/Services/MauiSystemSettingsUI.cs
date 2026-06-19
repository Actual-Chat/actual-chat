using ActualChat.UI;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="SystemSettingsUI"/> that opens platform system settings.
/// </summary>
public class MauiSystemSettingsUI : SystemSettingsUI
{
    [method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiSystemSettingsUI))]
    public MauiSystemSettingsUI() { }

    public override Task Open()
    {
        // AppInfo.ShowSettingsUI() deep-links to the iOS per-app Settings page (app-settings:),
        // which is a silent no-op on Mac Catalyst — macOS has no such page. Open the System
        // Settings privacy pane directly instead so users can unblock camera/mic access.
        if (OperatingSystem.IsMacCatalyst())
            return MauiBrowser.Open("x-apple.systempreferences:com.apple.preference.security?Privacy_Camera");

        AppInfo.Current.ShowSettingsUI();
        return Task.CompletedTask;
    }
}
