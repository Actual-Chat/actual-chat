namespace ActualChat.Chat.UI.Blazor.Components;

public interface IWindowsSettings
{
    Task<AutoStartupState> GetAutoStartupState();
    Task SetAutoStartup(bool isEnabled);
}

public record AutoStartupState(bool IsEnabled, bool CanChange = true, string CanNotChangeReason = "", Action? Fix = null);
