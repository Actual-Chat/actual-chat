namespace ActualChat.Chat.UI.Blazor.Services;

public interface INativeAppSettings
{
    Task<AutoStartState> GetAutoStartState();
    Task SetAutoStart(bool isEnabled);
}
