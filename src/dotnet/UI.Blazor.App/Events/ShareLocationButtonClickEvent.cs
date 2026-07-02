namespace ActualChat.UI.Blazor.App.Events;

public sealed record ShareLocationButtonClickEvent(string EditorId = "") : IUIEvent;
