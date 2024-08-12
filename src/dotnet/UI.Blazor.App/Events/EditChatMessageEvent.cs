namespace ActualChat.UI.Blazor.App.Events;

public sealed record EditChatMessageEvent(ChatEntry Entry) : IUIEvent;
