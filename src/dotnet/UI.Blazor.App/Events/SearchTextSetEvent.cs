namespace ActualChat.UI.Blazor.App.Events;

public sealed record SearchTextSetEvent(string Text) : IUIEvent;
