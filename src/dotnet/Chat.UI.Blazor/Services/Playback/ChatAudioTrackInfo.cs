using ActualChat.MediaPlayback;
using Cysharp.Text;

namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatAudioTrackInfo(ChatEntry AudioEntry) : TrackInfo(ComposeTrackId(AudioEntry))
{
    public static Symbol ComposeTrackId(ChatEntry entry)
        => entry.Kind == ChatEntryKind.Audio
            ? ComposeTrackId(entry.ChatId, entry.LocalId)
            : ComposeTrackId(entry.ChatId, entry.AudioEntryId ?? 0);

    public static Symbol ComposeTrackId(ChatId chatId, long audioEntryId)
        => ZString.Concat("audio:", chatId.Value, ":", audioEntryId);
}
