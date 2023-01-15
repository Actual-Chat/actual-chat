namespace ActualChat.Chat.UI.Blazor.Events;

public record EditChatMessageEvent(ChatEntry Entry) : IUIEvent;
