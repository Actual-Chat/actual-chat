namespace ActualChat.Chat.UI.Blazor.Events;

public record EditChatMessageEvent(ChatEntry ChatEntry) : IUIEvent;
