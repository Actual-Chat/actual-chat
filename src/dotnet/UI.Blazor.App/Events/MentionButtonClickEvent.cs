namespace ActualChat.UI.Blazor.App.Events;

public sealed record MentionButtonClickEvent(string EditorId = "") : IUIEvent;
