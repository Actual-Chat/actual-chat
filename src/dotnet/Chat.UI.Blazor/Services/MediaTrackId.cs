using Cysharp.Text;

namespace ActualChat.Chat.UI.Blazor.Services;

public static class MediaTrackId
{
    public static Symbol GetAudioTrackId(ChatEntry chatEntry)
        => ZString.Concat("audio:", chatEntry.ChatId, ":", chatEntry.AudioEntryId ?? chatEntry.Id);

    public static Symbol GetVideoTrackId(ChatEntry chatEntry)
        => ZString.Concat("video:", chatEntry.ChatId, ":", chatEntry.VideoEntryId ?? chatEntry.Id);
}
