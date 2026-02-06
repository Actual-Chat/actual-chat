namespace ActualChat.UI;

/// <summary>
/// Provides platform-specific access to system settings UI.
/// </summary>
public class SystemSettingsUI
{
    public virtual Task Open()
        => Task.CompletedTask;
}
