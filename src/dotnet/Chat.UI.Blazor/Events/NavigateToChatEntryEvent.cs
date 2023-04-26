namespace ActualChat.Chat.UI.Blazor.Events;

public sealed record NavigateToChatEntryEvent(ChatEntryId ChatEntryId) : IUIEvent;
