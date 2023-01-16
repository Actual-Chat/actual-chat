namespace ActualChat.Chat.UI.Blazor.Events;

public record SelectSortingTypeEvent(ChatListNavbarWidget.TabId TabId) : IUIEvent;
