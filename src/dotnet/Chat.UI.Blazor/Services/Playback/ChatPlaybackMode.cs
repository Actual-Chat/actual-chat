namespace ActualChat.Chat.UI.Blazor.Services;

public abstract record ChatPlaybackMode;

public record RealtimeChatPlaybackMode(ImmutableHashSet<Symbol> ChatIds) : ChatPlaybackMode;

public record HistoricalChatPlaybackMode(Symbol ChatId, Moment StartAt) : ChatPlaybackMode;
