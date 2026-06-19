namespace ActualChat.UI;

// macOS groups privacy settings by category (a distinct pane per section); iOS/Android
// group them under a single per-app page, so the section is only used on Mac Catalyst.
public enum SystemSettingsSection { App, Camera, Microphone }

/// <summary>
/// Provides platform-specific access to system settings UI.
/// </summary>
public class SystemSettingsUI
{
    public virtual Task Open(SystemSettingsSection section = SystemSettingsSection.App)
        => Task.CompletedTask;
}
