namespace ActualChat.UI;

/// <summary>
/// A system settings pane to open. Only macOS can target a pane,
/// every other platform opens the app's own settings page for any value.
/// </summary>
public enum SystemSettingsPane
{
    App = 0,
    Microphone,
    Camera,
    Contacts,
    Location,
    Notifications,
    Photos,
}
