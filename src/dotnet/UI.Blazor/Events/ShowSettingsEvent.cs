namespace ActualChat.UI.Blazor.Events;

public sealed record ShowSettingsEvent(string? TabId = null) : IUIEvent;
