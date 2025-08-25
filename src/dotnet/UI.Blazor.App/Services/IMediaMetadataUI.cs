namespace ActualChat.UI.Blazor.App.Services;

public record MediaMetadata(
    string Title,
    string Artist,
    string ImageUrl)
{
    public static MediaMetadata FromTrack(ChatAudioTrackInfo trackInfo)
        => new (
            $"{trackInfo.Author.Avatar.Name} @ {trackInfo.Chat.Title}",
            "Voxt",
            "/_applogo-dark.svg");
}

public interface IMediaMetadataUI
{
    public void SetPlayback(MediaMetadata metadata, bool isStreaming);
    public void SetRecording(MediaMetadata metadata);
    public void Reset();
}
