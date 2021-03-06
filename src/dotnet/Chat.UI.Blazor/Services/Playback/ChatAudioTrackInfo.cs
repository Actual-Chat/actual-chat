using ActualChat.MediaPlayback;
using Cysharp.Text;

namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatAudioTrackInfo(ChatEntry AudioEntry) : TrackInfo(ComposeTrackId(AudioEntry))
{
    public static Symbol ComposeTrackId(ChatEntry entry)
        => entry.Type == ChatEntryType.Audio
            ? ZString.Concat("audio:", entry.ChatId, ":", entry.Id)
            : ComposeTrackId(entry.ChatId, entry.AudioEntryId ?? 0);

    public static Symbol ComposeTrackId(Symbol chatId, long audioEntryId)
        => ZString.Concat("audio:", chatId.Value, ":", audioEntryId);
}
