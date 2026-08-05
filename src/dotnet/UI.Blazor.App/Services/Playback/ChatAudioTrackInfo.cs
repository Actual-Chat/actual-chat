using ActualChat.MediaPlayback;

namespace ActualChat.UI.Blazor.App.Services;

public record ChatAudioTrackInfo : TrackInfo
{
    public ChatEntry? AudioEntry { get; }
    public Chat.Chat? Chat { get; }
    public Author? Author { get; }
    public ChatEntryId? EntryId { get; }
    public string StreamId { get; init; } = "";

    // Primary constructor for entry-based playback
    public ChatAudioTrackInfo(ChatEntry audioEntry, Chat.Chat chat, Author author)
        : base(ComposeTrackId(audioEntry), audioEntry.IsContentStreaming)
    {
        AudioEntry = audioEntry;
        Chat = chat;
        Author = author;
        EntryId = audioEntry.Id;
    }

    // Constructor for RTC stream-based playback (always streaming)
    public ChatAudioTrackInfo(ChatId chatId, ChatEntryId? entryId, Chat.Chat? chat, Author? author)
        : base(ComposeTrackId(chatId, entryId), true)
    {
        Chat = chat;
        Author = author;
        EntryId = entryId;
    }

    public static Symbol ComposeTrackId(ChatEntry entry)
        => ComposeTrackId(entry.ChatId, entry.LocalId);

    public static Symbol ComposeTrackId(ChatId chatId, long audioEntryId)
        => $"audio:{chatId.Value}:{audioEntryId}";

    public static Symbol ComposeTrackId(ChatId chatId, ChatEntryId? entryId)
        => entryId != null
            ? $"audio:{entryId.Value}"
            : $"audio:{chatId.Value}:rtc-{Guid.NewGuid():N}";
}
