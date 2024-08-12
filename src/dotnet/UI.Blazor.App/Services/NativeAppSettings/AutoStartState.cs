namespace ActualChat.UI.Blazor.App.Services;

public record AutoStartState(
    bool IsEnabled,
    bool CanChange = true,
    string CanNotChangeReason = "",
    Action? Fix = null);
