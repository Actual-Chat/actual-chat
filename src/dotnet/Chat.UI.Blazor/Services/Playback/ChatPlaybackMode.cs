namespace ActualChat.Chat.UI.Blazor.Services;

public abstract record ChatPlaybackMode;

public record RealtimeChatPlaybackMode(
    ImmutableHashSet<Symbol> ChatIds,
    bool IsPlayingPinned) : ChatPlaybackMode;

public record HistoricalChatPlaybackMode(
    Symbol ChatId,
    Moment StartAt,
    RealtimeChatPlaybackMode? PreviousMode) : ChatPlaybackMode;
