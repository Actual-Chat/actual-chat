namespace ActualChat.UI;

/// <summary>
/// Provides platform-specific access to system settings UI.
/// </summary>
public class SystemSettingsUI
{
    public Task Open()
        => Open(SystemSettingsPane.App);

    public virtual Task Open(SystemSettingsPane pane)
        => Task.CompletedTask;
}
