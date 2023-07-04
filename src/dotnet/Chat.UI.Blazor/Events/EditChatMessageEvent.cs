namespace ActualChat.Chat.UI.Blazor.Events;

public sealed record EditChatMessageEvent(ChatEntry Entry) : IUIEvent;
