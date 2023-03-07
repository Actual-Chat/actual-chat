namespace ActualChat.Chat.UI.Blazor.Events;

public record NavigateToChatEntryEvent(ChatEntryId ChatEntryId) : IUIEvent;
