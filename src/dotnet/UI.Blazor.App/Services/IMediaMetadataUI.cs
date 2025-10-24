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
            "/_applogo-dark_voxt.svg");
}

public interface IMediaMetadataUI
{
    public Task SetPlayback(MediaMetadata metadata, bool isStreaming);
    public Task SetRecording(MediaMetadata metadata);
    public Task Reset();
}
