using ActualChat.MediaPlayback;
using Cysharp.Text;

namespace ActualChat.UI.Blazor.App.Services;

public record ChatAudioTrackInfo(ChatEntry AudioEntry, Chat.Chat Chat, Author Author) : TrackInfo(ComposeTrackId(AudioEntry))
{
    public static Symbol ComposeTrackId(ChatEntry entry)
        => entry.Kind == ChatEntryKind.Audio
            ? ComposeTrackId(entry.ChatId, entry.LocalId)
            : ComposeTrackId(entry.ChatId, entry.AudioEntryLid ?? 0);

    public static Symbol ComposeTrackId(ChatId chatId, long audioEntryId)
        => ZString.Concat("audio:", chatId.Value, ":", audioEntryId);
}
