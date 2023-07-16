namespace ActualChat.Chat.UI.Blazor.Services;

public record AutoStartState(
    bool IsEnabled,
    bool CanChange = true,
    string CanNotChangeReason = "",
    Action? Fix = null);
