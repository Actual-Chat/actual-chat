namespace ActualChat.UI.Blazor.App.Events;

public sealed record NavigateToChatEntryEvent(
    ChatEntryId ChatEntryId,
    bool MustHighlight
    ) : IUIEvent;
