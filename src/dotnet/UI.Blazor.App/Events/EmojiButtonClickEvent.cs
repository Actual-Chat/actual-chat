namespace ActualChat.UI.Blazor.App.Events;

public sealed record EmojiButtonClickEvent(string EditorId = "") : IUIEvent;
