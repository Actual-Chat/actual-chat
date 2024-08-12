namespace ActualChat.UI.Blazor.App.Services;

public interface INativeAppSettings
{
    Task<AutoStartState> GetAutoStartState();
    Task SetAutoStart(bool isEnabled);
}
