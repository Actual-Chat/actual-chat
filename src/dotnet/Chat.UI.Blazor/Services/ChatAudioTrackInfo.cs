using ActualChat.MediaPlayback;
using Cysharp.Text;

namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatAudioTrackInfo(ChatEntry AudioEntry) : TrackInfo(ComposeTrackId(AudioEntry))
{
    public static Symbol ComposeTrackId(ChatEntry audioEntry)
        => audioEntry.Type == ChatEntryType.Audio
            ? ZString.Concat("audio:", audioEntry.ChatId, ":", audioEntry.Id)
            : ComposeTrackId(audioEntry.ChatId, audioEntry.Id);

    public static Symbol ComposeTrackId(Symbol chatId, long audioEntryId)
        => ZString.Concat("audio:", chatId.Value, ":", audioEntryId);
}
